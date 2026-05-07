using System.Globalization;
using System.Text.Json;
using StackExchange.Redis;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Services;

namespace ZetAuction.Infrastructure.Redis;

/// <summary>
/// Redis-backed cache for the "highest bid" projection. Writes are
/// version-checked atomically with a Lua script: a payload only
/// overwrites the previous one if its Amount strictly exceeds what is
/// already stored. This eliminates the ordering hazard introduced by
/// near-simultaneous bids — without the check, a bid with amount X
/// arriving at the cache after a competing bid X+1 would silently
/// downgrade the visible high.
/// </summary>
/// <remarks>
/// The cache stores two hash fields: <c>amount</c> (the comparison key)
/// and <c>payload</c> (the serialized BidViewModel returned to API
/// clients). Both are written together inside the script so readers
/// never see a torn pair.
/// </remarks>
public sealed class RedisCacheService : IBidCacheService
{
    private const string AmountField = "amount";
    private const string PayloadField = "payload";
    private const int TtlSeconds = 30;

    private const string VersionedSetLuaScript = """
        local key = KEYS[1]
        local newAmount = tonumber(ARGV[1])
        local newPayload = ARGV[2]
        local ttl = tonumber(ARGV[3])

        local storedAmount = redis.call('HGET', key, 'amount')
        if storedAmount and tonumber(storedAmount) and tonumber(storedAmount) >= newAmount then
            return 0
        end

        redis.call('HSET', key, 'amount', tostring(newAmount), 'payload', newPayload)
        redis.call('EXPIRE', key, ttl)
        return 1
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly RedisConnectionManager _connectionManager;

    public RedisCacheService(RedisConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    public async Task<BidViewModel?> GetHighestBidAsync(Guid auctionId)
    {
        var key = GetCacheKey(auctionId);
        var payload = await _connectionManager.GetDatabase().HashGetAsync(key, PayloadField);

        if (payload.IsNullOrEmpty)
            return null;

        return JsonSerializer.Deserialize<BidViewModel>(payload!, JsonOptions);
    }

    public async Task SetHighestBidAsync(Guid auctionId, BidViewModel bid)
    {
        var key = GetCacheKey(auctionId);
        var payload = JsonSerializer.Serialize(bid, JsonOptions);
        var amount = bid.Amount.ToString("0.############", CultureInfo.InvariantCulture);

        // The Lua script returns 1 if the new value won the version
        // check, 0 if it was rejected because the cache already holds a
        // higher amount. We do not throw on rejection — losing a write
        // is the *intended* behaviour. The DB remains the source of
        // truth and the cache will reconverge on the next successful
        // write or on TTL expiry.
        await _connectionManager.GetDatabase().ScriptEvaluateAsync(
            VersionedSetLuaScript,
            new RedisKey[] { key },
            new RedisValue[] { amount, payload, TtlSeconds });
    }

    public async Task InvalidateHighestBidAsync(Guid auctionId)
    {
        var key = GetCacheKey(auctionId);
        await _connectionManager.GetDatabase().KeyDeleteAsync(key);
    }

    private static string GetCacheKey(Guid auctionId) => $"auction:{auctionId}:highest-bid";
}
