using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Paramore.Brighter.Outbox.PostgreSql;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Repositories;
using ZetAuction.Infrastructure.Auth;
using ZetAuction.Infrastructure.Persistence;
using ZetAuction.Infrastructure.Persistence.Brighter;
using ZetAuction.Infrastructure.Persistence.Interceptors;
using ZetAuction.Infrastructure.Persistence.Repositories;
using ZetAuction.Infrastructure.Redis;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Logical name of the Brighter outbox table. Lowercase to match
    /// PostgreSQL's default folding so the table name doesn't need quoting
    /// in raw SQL.
    /// </summary>
    public const string BrighterOutboxTableName = "outbox_messages";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new InvalidOperationException(
                "Connection string 'DefaultConnection' is not configured. " +
                "Set ConnectionStrings__DefaultConnection to a valid Npgsql connection string.");

        // Brighter outbox configuration is shared by the SaveChanges
        // interceptor (producer) and the dispatcher worker (consumer).
        // Singleton — connection string + table name don't change at runtime.
        services.AddSingleton(new PostgreSqlOutboxConfiguration(connectionString, BrighterOutboxTableName));

        // The interceptor needs the outbox configuration for ad-hoc
        // PostgreSqlOutbox instantiation per save. Scoped — same lifetime
        // as the DbContext it intercepts.
        services.AddScoped<DomainEventsSaveChangesInterceptor>();

        services.AddDbContext<ZetAuctionDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(ZetAuctionDbContext).Assembly.FullName);
                // Intentionally NOT calling EnableRetryOnFailure — the
                // outbox dispatcher and finalizer workers manage their
                // own explicit transactions, and Npgsql's retrying
                // execution strategy refuses to coexist with
                // user-initiated transactions. Application-layer Polly
                // retry already covers the optimistic-concurrency
                // class of failures (ADR-0005).
            });
            // Resolve the interceptor through DI so PostgreSqlOutboxConfiguration
            // (and any future dependencies) flow in cleanly.
            options.AddInterceptors(serviceProvider.GetRequiredService<DomainEventsSaveChangesInterceptor>());
        });

        // EF-bridged connection provider for the outbox dispatcher worker.
        // Shares the DbContext-owned NpgsqlConnection so reads see committed
        // outbox rows without opening a second pool connection.
        services.AddScoped<EntityFrameworkPostgreSqlConnectionProvider>();

        // PostgreSqlOutbox is the consumer-side handle used by the
        // BrighterOutboxDispatcherWorker. Scoped — borrows the EF DbContext
        // through the connection provider above.
        services.AddScoped(sp =>
        {
            var config = sp.GetRequiredService<PostgreSqlOutboxConfiguration>();
            var provider = sp.GetRequiredService<EntityFrameworkPostgreSqlConnectionProvider>();
            return new PostgreSqlOutbox(config, provider);
        });

        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAuctionRepository, AuctionRepository>();
        services.AddScoped<IBidRepository, BidRepository>();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        var redisConnectionString = configuration.GetConnectionString("Redis")
            ?? throw new InvalidOperationException(
                "Connection string 'Redis' is not configured. " +
                "Set ConnectionStrings__Redis to a reachable Redis endpoint.");

        services.AddSingleton<RedisConnectionManager>(_ =>
            new RedisConnectionManager(redisConnectionString));

        services.AddSingleton<IBidCacheService, RedisCacheService>();
        services.AddSingleton<IRateLimiterService, RedisRateLimiterService>();

        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IPasswordHasher, PasswordHasher>();

        return services;
    }
}
