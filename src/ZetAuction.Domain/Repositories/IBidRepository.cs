using ZetAuction.Domain.Bids;

namespace ZetAuction.Domain.Repositories;

public interface IBidRepository : IRepository<Bid>
{
    Task<IEnumerable<Bid>> GetByAuctionIdAsync(Guid auctionId, CancellationToken cancellationToken = default);
    Task<Bid?> GetHighestBidAsync(Guid auctionId, CancellationToken cancellationToken = default);
    Task<IEnumerable<Bid>> GetUserBidsAsync(Guid userId, CancellationToken cancellationToken = default);
}
