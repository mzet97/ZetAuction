using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class BidReadRepository : IBidReadRepository
{
    private readonly ZetAuctionDbContext _context;

    public BidReadRepository(ZetAuctionDbContext context)
    {
        _context = context;
    }

    public async Task<BaseResultList<BidHistoryDto>> GetBidHistoryAsync(Guid auctionId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        // Use anonymous type projection first, then map to DTO in memory to avoid EF translation issues
        var query = from bid in _context.Bids.AsNoTracking()
                    join user in _context.Users.AsNoTracking() on bid.UserId equals user.Id into users
                    from user in users.DefaultIfEmpty()
                    where bid.AuctionId == auctionId
                    select new
                    {
                        bid.Id,
                        bid.AuctionId,
                        bid.UserId,
                        bid.Amount,
                        bid.PlacedAtUtc,
                        UserName = user == null ? "Unknown" : user.Name
                    };

        var totalCount = await query.CountAsync(cancellationToken);
        var pagedResult = PagedResult.Create(page, pageSize, totalCount);

        var bids = await query
            .OrderByDescending(b => b.PlacedAtUtc)
            .Skip(pagedResult.Skip())
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var dtoList = bids.Select(b => new BidHistoryDto(
            b.Id, b.AuctionId, b.UserId, b.Amount, b.PlacedAtUtc, b.UserName)).ToList();

        return BaseResultList<BidHistoryDto>.Ok(dtoList, pagedResult);
    }

    public async Task<BidHistoryDto?> GetHighestBidAsync(Guid auctionId, CancellationToken cancellationToken = default)
    {
        // SQLite doesn't support decimal in ORDER BY, so we cast to double
        var result = await (from bid in _context.Bids.AsNoTracking()
                            join user in _context.Users.AsNoTracking() on bid.UserId equals user.Id into users
                            from user in users.DefaultIfEmpty()
                            where bid.AuctionId == auctionId
                            orderby (double)bid.Amount descending
                            select new
                            {
                                bid.Id,
                                bid.AuctionId,
                                bid.UserId,
                                bid.Amount,
                                bid.PlacedAtUtc,
                                UserName = user == null ? "Unknown" : user.Name
                            })
            .FirstOrDefaultAsync(cancellationToken);

        return result == null ? null : new BidHistoryDto(
            result.Id, result.AuctionId, result.UserId, result.Amount, result.PlacedAtUtc, result.UserName);
    }
}
