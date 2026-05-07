using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using Respawn;
using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Testcontainers.PostgreSql;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Users;
using ZetAuction.Infrastructure.Auth;
using ZetAuction.Infrastructure.Persistence;

namespace ZetAuction.IntegrationTests;

/// <summary>
/// Web application factory backed by an ephemeral Postgres 16 container
/// (Testcontainers) so integration tests run against the same engine as
/// production. Ships migrations on startup and exposes a Respawn-based reset
/// hook so test classes can isolate side effects without recreating the
/// container.
/// </summary>
public sealed class CustomWebApplicationFactory : WebApplicationFactory<ZetAuction.Api.Program>, IAsyncLifetime
{
    public const string SeededUserEmail = "integration.user@example.com";
    public const string SeededUserPassword = "Password123!";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .WithDatabase("zetauction_tests")
        .WithUsername("zetauction")
        .WithPassword("zetauction")
        .WithCleanUp(true)
        .Build();

    private Respawner? _respawner;

    public Guid SeededUserId { get; } = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public Guid SeededAuctionId { get; } = Guid.Parse("22222222-2222-2222-2222-222222222222");

    internal void ResetRateLimiter()
    {
        Services.GetRequiredService<TestRateLimiterService>().Reset();
    }

    /// <summary>
    /// Truncates every non-system table so a test class can start from a
    /// known baseline without bouncing the Postgres container. Re-seeds the
    /// canonical user/auction afterwards to preserve <c>SeededUserId</c> /
    /// <c>SeededAuctionId</c> contracts the existing tests rely on.
    /// </summary>
    public async Task ResetDatabaseAsync()
    {
        if (_respawner is null) return;

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        await _respawner.ResetAsync(connection);

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
        SeedDatabase(dbContext);
    }

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        using var scope = Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
        await dbContext.Database.MigrateAsync();
        SeedDatabase(dbContext);

        await using var connection = new NpgsqlConnection(_postgres.GetConnectionString());
        await connection.OpenAsync();
        _respawner = await Respawner.CreateAsync(connection, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = [new Respawn.Graph.Table("__EFMigrationsHistory")]
        });
    }

    public new async Task DisposeAsync()
    {
        await _postgres.DisposeAsync();
        await base.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configurationBuilder) =>
        {
            configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                ["ConnectionStrings:Redis"] = "localhost:6379",
                ["Jwt:Issuer"] = "ZetAuction.Tests",
                ["Jwt:Audience"] = "ZetAuction.Tests",
                ["Jwt:SecretKey"] = "test-secret-key-with-at-least-32-characters-long",
                ["Jwt:ExpirationHours"] = "1"
            });
        });

        builder.ConfigureServices(services =>
        {
            // Replace the production DbContext registration with one that
            // points at this fixture's ephemeral Testcontainers Postgres.
            // Doing it in DI (rather than via configuration) survives any
            // ordering quirks between the host's appsettings.json defaults
            // and the test factory's configuration overrides, and is also
            // immune to env-var pollution across parallel test classes.
            services.RemoveAll<DbContextOptions<ZetAuctionDbContext>>();
            services.RemoveAll(typeof(DbContextOptions));
            services.AddDbContext<ZetAuctionDbContext>(options =>
            {
                options.UseNpgsql(_postgres.GetConnectionString(), npgsql =>
                    npgsql.MigrationsAssembly(typeof(ZetAuctionDbContext).Assembly.FullName));
            });

            // Replace hosted services and external dependencies with test
            // doubles so the integration tests do not require Redis or the
            // background finalisation worker.
            services.RemoveAll<Microsoft.Extensions.Hosting.IHostedService>();
            services.RemoveAll<IRateLimiterService>();
            services.RemoveAll<IBidCacheService>();

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = TestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = TestAuthHandler.SchemeName;
            }).AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });

            services.AddSingleton<TestRateLimiterService>();
            services.AddSingleton<IRateLimiterService>(sp => sp.GetRequiredService<TestRateLimiterService>());
            services.AddSingleton<IBidCacheService, TestBidCacheService>();
        });
    }

    private void SeedDatabase(ZetAuctionDbContext dbContext)
    {
        var passwordHasher = new PasswordHasher();
        var now = DateTime.UtcNow;

        if (!dbContext.Users.Any(u => u.Id == SeededUserId))
        {
            var user = new User("Integration User", SeededUserEmail, passwordHasher.HashPassword(SeededUserPassword), Role.User);
            typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, SeededUserId);
            user.MarkCreated(now);
            dbContext.Users.Add(user);
        }

        if (!dbContext.Auctions.Any(a => a.Id == SeededAuctionId))
        {
            var auction = new Auction(
                "Seed Auction",
                "Seed auction description",
                startingPrice: 100m,
                minBidIncrement: 10m,
                endDate: now.AddDays(7),
                createdByUserId: SeededUserId);
            typeof(Auction).GetProperty(nameof(Auction.Id))!.SetValue(auction, SeededAuctionId);
            auction.MarkCreated(now);
            auction.Activate();
            dbContext.Auctions.Add(auction);
        }

        dbContext.SaveChanges();
    }
}

internal sealed class TestRateLimiterService : IRateLimiterService
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _requests = new();
    private readonly object _gate = new();

    public Task<bool> IsAllowedAsync(string key, int maxRequests, int windowSeconds)
    {
        var now = DateTime.UtcNow;
        var windowStart = now.AddSeconds(-windowSeconds);

        lock (_gate)
        {
            var requests = _requests.GetOrAdd(key, _ => new Queue<DateTime>());

            while (requests.Count > 0 && requests.Peek() <= windowStart)
            {
                requests.Dequeue();
            }

            if (requests.Count >= maxRequests)
            {
                return Task.FromResult(false);
            }

            requests.Enqueue(now);
            return Task.FromResult(true);
        }
    }

    public void Reset()
    {
        _requests.Clear();
    }
}

internal sealed class TestBidCacheService : IBidCacheService
{
    private readonly ConcurrentDictionary<Guid, BidViewModel> _cache = new();

    public Task<BidViewModel?> GetHighestBidAsync(Guid auctionId)
    {
        _cache.TryGetValue(auctionId, out var bid);
        return Task.FromResult(bid);
    }

    public Task SetHighestBidAsync(Guid auctionId, BidViewModel bid)
    {
        _cache.AddOrUpdate(auctionId, bid, (_, existing) => bid.Amount >= existing.Amount ? bid : existing);
        return Task.CompletedTask;
    }

    public Task InvalidateHighestBidAsync(Guid auctionId)
    {
        _cache.TryRemove(auctionId, out _);
        return Task.CompletedTask;
    }
}

internal sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Test";

    public TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : base(options, logger, encoder)
    {
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var authorization) ||
            !authorization.ToString().StartsWith(SchemeName, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var userId = authorization.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1)
            ?? CustomWebApplicationFactory.SeededUserEmail;

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId),
            new Claim(ClaimTypes.Email, CustomWebApplicationFactory.SeededUserEmail),
            new Claim(ClaimTypes.Role, Role.User.ToString())
        };
        var identity = new ClaimsIdentity(claims, SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
