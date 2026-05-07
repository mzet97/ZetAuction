using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using StackExchange.Redis;
using ZetAuction.Application.Services;

namespace ZetAuction.Infrastructure.Redis;

/// <summary>
/// Distributed sliding-window rate limiter backed by Redis sorted sets
/// and a Lua script (atomic ZREMRANGEBYSCORE + ZCARD + ZADD). When
/// Redis is unreachable or slow, requests are diverted to a local
/// fail-degraded fallback instead of the previous silent fail-open
/// path: callers still hit a limit, just a tighter one, and the
/// transition is observable through warning logs.
/// </summary>
/// <remarks>
/// The fallback budget is intentionally smaller than the distributed
/// budget (configured via <c>fallbackMaxRequests</c>) because each
/// replica enforces it independently — a 5-replica deployment with a
/// per-replica budget of 2/5min still gives each user at most 10
/// bids/5min instead of the global 5. That degradation is the price
/// of keeping the bid endpoint available during a Redis outage; for a
/// money-handling endpoint it is the correct trade-off.
/// </remarks>
public sealed class RedisRateLimiterService : IRateLimiterService
{
    private const string RateLimiterLuaScript = """
        local window = tonumber(ARGV[1])
        local limit = tonumber(ARGV[2])
        local now = tonumber(ARGV[3])
        local member = ARGV[4]

        redis.call('ZREMRANGEBYSCORE', KEYS[1], 0, now - window)
        local current = redis.call('ZCARD', KEYS[1])

        if current >= limit then
            return 0
        end

        redis.call('ZADD', KEYS[1], now, member)
        redis.call('EXPIRE', KEYS[1], math.ceil(window / 1000))
        return 1
        """;

    private readonly RedisConnectionManager _connectionManager;
    private readonly ILogger<RedisRateLimiterService> _logger;
    private readonly ResiliencePipeline _circuitBreaker;
    private readonly InMemoryRateLimiter _localFallback;
    private readonly int _fallbackMaxRequests;

    public RedisRateLimiterService(
        RedisConnectionManager connectionManager,
        ILogger<RedisRateLimiterService> logger,
        int fallbackMaxRequests = 2)
    {
        _connectionManager = connectionManager;
        _logger = logger;
        _localFallback = new InMemoryRateLimiter();
        _fallbackMaxRequests = fallbackMaxRequests;

        _circuitBreaker = new ResiliencePipelineBuilder()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions
            {
                ShouldHandle = new PredicateBuilder()
                    .Handle<RedisConnectionException>()
                    .Handle<RedisTimeoutException>(),
                FailureRatio = 0.5,
                MinimumThroughput = 4,
                SamplingDuration = TimeSpan.FromSeconds(15),
                BreakDuration = TimeSpan.FromSeconds(15),
                OnOpened = args =>
                {
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Redis circuit breaker opened for rate limiter; falling back to in-memory budget {Budget} per window",
                        _fallbackMaxRequests);
                    return ValueTask.CompletedTask;
                },
                OnClosed = _ =>
                {
                    _logger.LogInformation("Redis circuit breaker closed; rate limiter resumed against Redis");
                    return ValueTask.CompletedTask;
                },
                OnHalfOpened = _ =>
                {
                    _logger.LogInformation("Redis circuit breaker half-open; probing");
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public async Task<bool> IsAllowedAsync(string key, int maxRequests, int windowSeconds)
    {
        try
        {
            return await _circuitBreaker.ExecuteAsync(async ct =>
            {
                return await CheckRedisAsync(key, maxRequests, windowSeconds);
            });
        }
        catch (BrokenCircuitException)
        {
            // Circuit is open: do not even try Redis. Use local fallback
            // with a tighter budget so an outage cannot turn into an
            // unbounded acceptance window.
            return _localFallback.IsAllowed(key, _fallbackMaxRequests, windowSeconds);
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Redis connection failure on rate limiter; using local fallback");
            return _localFallback.IsAllowed(key, _fallbackMaxRequests, windowSeconds);
        }
        catch (RedisTimeoutException ex)
        {
            _logger.LogWarning(ex, "Redis timeout on rate limiter; using local fallback");
            return _localFallback.IsAllowed(key, _fallbackMaxRequests, windowSeconds);
        }
    }

    private async Task<bool> CheckRedisAsync(string key, int maxRequests, int windowSeconds)
    {
        var redisKey = $"ratelimit:{key}";
        var windowMilliseconds = windowSeconds * 1000;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var member = $"{now}:{Guid.NewGuid():N}";

        var result = await _connectionManager.GetDatabase().ScriptEvaluateAsync(
            RateLimiterLuaScript,
            new RedisKey[] { redisKey },
            new RedisValue[] { windowMilliseconds, maxRequests, now, member });

        return (long)result! == 1;
    }

    /// <summary>
    /// Process-local sliding-window rate limiter used while the circuit
    /// breaker is open. Locks are partitioned by key to keep contention
    /// proportional to the number of distinct callers, not to the
    /// global throughput.
    /// </summary>
    private sealed class InMemoryRateLimiter
    {
        private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();

        public bool IsAllowed(string key, int maxRequests, int windowSeconds)
        {
            var now = DateTime.UtcNow;
            var windowStart = now.AddSeconds(-windowSeconds);

            var window = _windows.GetOrAdd(key, _ => new Queue<DateTime>());
            lock (window)
            {
                while (window.Count > 0 && window.Peek() <= windowStart)
                {
                    window.Dequeue();
                }

                if (window.Count >= maxRequests)
                {
                    return false;
                }

                window.Enqueue(now);
                return true;
            }
        }
    }
}
