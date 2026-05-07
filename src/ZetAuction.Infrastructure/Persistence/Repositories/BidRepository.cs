using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class BidRepository : Repository<Bid>, IBidRepository
{
    public BidRepository(ZetAuctionDbContext context, IDateTimeProvider dateTimeProvider)
        : base(context, dateTimeProvider)
    {
    }

    public async Task<IEnumerable<Bid>> GetByAuctionIdAsync(
        Guid auctionId, CancellationToken cancellationToken = default)
    {
        return await Db.Bids
            .AsNoTracking()
            .Where(b => b.AuctionId == auctionId)
            .OrderByDescending(b => b.PlacedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<Bid?> GetHighestBidAsync(
        Guid auctionId, CancellationToken cancellationToken = default)
    {
        return await Db.Bids
            .AsNoTracking()
            .Where(b => b.AuctionId == auctionId)
            .OrderByDescending(b => (double)b.Amount)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IEnumerable<Bid>> GetUserBidsAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        return await Db.Bids
            .AsNoTracking()
            .Where(b => b.UserId == userId)
            .OrderByDescending(b => b.PlacedAtUtc)
            .ToListAsync(cancellationToken);
    }
}
