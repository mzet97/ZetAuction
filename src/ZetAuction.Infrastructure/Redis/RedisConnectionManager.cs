using StackExchange.Redis;
using System.Text.Json;

namespace ZetAuction.Infrastructure.Redis;

public sealed class RedisConnectionManager : IAsyncDisposable
{
    private readonly Lazy<ConnectionMultiplexer> _connection;
    private readonly string _connectionString;

    public RedisConnectionManager(string connectionString)
    {
        _connectionString = connectionString;
        var options = ConfigurationOptions.Parse(connectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 5000;
        options.SyncTimeout = 5000;
        _connection = new Lazy<ConnectionMultiplexer>(() => ConnectionMultiplexer.Connect(options));
    }

    public IDatabase GetDatabase() => _connection.Value.GetDatabase();

    public ConnectionMultiplexer GetConnection() => _connection.Value;

    public async ValueTask DisposeAsync()
    {
        if (_connection.IsValueCreated)
        {
            await _connection.Value.DisposeAsync();
        }
    }
}
