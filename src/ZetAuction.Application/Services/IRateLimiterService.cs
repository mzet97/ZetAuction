namespace ZetAuction.Application.Services;

public interface IRateLimiterService
{
    Task<bool> IsAllowedAsync(string key, int maxRequests, int windowSeconds);
}
