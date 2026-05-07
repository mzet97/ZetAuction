using ZetAuction.Shared.Responses;

namespace ZetAuction.Domain.Repositories;

public interface IBidReadRepository
{
    Task<BaseResultList<BidHistoryDto>> GetBidHistoryAsync(Guid auctionId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BidHistoryDto?> GetHighestBidAsync(Guid auctionId, CancellationToken cancellationToken = default);
}
