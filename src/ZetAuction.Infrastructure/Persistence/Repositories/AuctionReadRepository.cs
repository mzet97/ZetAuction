using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class AuctionReadRepository : IAuctionReadRepository
{
    private readonly ZetAuctionDbContext _context;

    public AuctionReadRepository(ZetAuctionDbContext context)
    {
        _context = context;
    }

    public async Task<AuctionDetailsDto?> GetDetailsByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Auctions
            .AsNoTracking()
            .Where(a => a.Id == id)
            .Select(a => new AuctionDetailsDto(
                a.Id,
                a.Title,
                a.Description,
                a.StartingPrice,
                a.CurrentPrice,
                a.EndDate,
                a.Status.ToString(),
                a.CreatedByUserId,
                a.WinnerId,
                a.Bids.Count,
                a.MinBidIncrement,
                a.WinningBidId,
                a.WinningAmount,
                a.FinalizedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<BaseResultList<AuctionListItemDto>> ListActiveAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        return ListByStatusAsync(AuctionStatus.Active, page, pageSize, cancellationToken);
    }

    public async Task<BaseResultList<AuctionListItemDto>> ListAllAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = _context.Auctions.AsNoTracking();

        return await ListAsync(query, page, pageSize, cancellationToken);
    }

    public async Task<BaseResultList<AuctionListItemDto>> ListByStatusAsync(AuctionStatus status, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = _context.Auctions
            .AsNoTracking()
            .Where(a => a.Status == status);

        return await ListAsync(query, page, pageSize, cancellationToken);
    }

    private static async Task<BaseResultList<AuctionListItemDto>> ListAsync(IQueryable<Auction> query, int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await query.CountAsync(cancellationToken);
        var pagedResult = PagedResult.Create(page, pageSize, totalCount);

        var auctions = await query
            .OrderByDescending(a => a.CreatedAtUtc)
            .Skip(pagedResult.Skip())
            .Take(pageSize)
            .Select(a => new AuctionListItemDto(
                a.Id,
                a.Title,
                a.Description,
                a.StartingPrice,
                a.CurrentPrice,
                a.EndDate,
                a.Status.ToString(),
                a.CreatedByUserId,
                a.WinnerId,
                a.Bids.Count,
                a.MinBidIncrement,
                a.WinningBidId,
                a.WinningAmount,
                a.FinalizedAtUtc))
            .ToListAsync(cancellationToken);

        return BaseResultList<AuctionListItemDto>.Ok(auctions, pagedResult);
    }
}
