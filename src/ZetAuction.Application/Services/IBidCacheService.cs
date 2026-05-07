using ZetAuction.Application.Bids.ViewModels;

namespace ZetAuction.Application.Services;

public interface IBidCacheService
{
    Task<BidViewModel?> GetHighestBidAsync(Guid auctionId);

    Task SetHighestBidAsync(Guid auctionId, BidViewModel bid);

    Task InvalidateHighestBidAsync(Guid auctionId);
}
