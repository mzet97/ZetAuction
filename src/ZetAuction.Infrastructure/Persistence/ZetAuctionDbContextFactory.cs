using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ZetAuction.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by <c>dotnet ef migrations</c>. Reads the
/// connection string from the API project's <c>appsettings.json</c> /
/// environment variables. Falls back to a local Postgres at
/// <c>localhost:5432</c> when none is configured so contributors can run
/// <c>dotnet ef</c> against the compose-managed Postgres without setting
/// extra environment variables.
/// </summary>
public class ZetAuctionDbContextFactory : IDesignTimeDbContextFactory<ZetAuctionDbContext>
{
    private const string FallbackConnectionString =
        "Host=localhost;Port=5432;Database=zetauction;Username=zetauction;Password=zetauction;Include Error Detail=true";

    public ZetAuctionDbContext CreateDbContext(string[] args)
    {
        var apiProjectPath = Path.Combine(Directory.GetCurrentDirectory(), "..", "ZetAuction.Api");

        if (!Directory.Exists(apiProjectPath))
        {
            apiProjectPath = Directory.GetCurrentDirectory();
        }

        var configuration = new ConfigurationBuilder()
            .SetBasePath(apiProjectPath)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables()
            .Build();

        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = FallbackConnectionString;
        }

        var optionsBuilder = new DbContextOptionsBuilder<ZetAuctionDbContext>();

        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
        });

        optionsBuilder.UseLoggerFactory(loggerFactory);
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(ZetAuctionDbContext).Assembly.FullName);
        });

        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
        if (environment == "Development")
        {
            optionsBuilder.EnableSensitiveDataLogging();
            optionsBuilder.EnableDetailedErrors();
        }

        return new ZetAuctionDbContext(optionsBuilder.Options);
    }
}
