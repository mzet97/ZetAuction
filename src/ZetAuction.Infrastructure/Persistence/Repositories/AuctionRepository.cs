using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class AuctionRepository : Repository<Auction>, IAuctionRepository
{
    public AuctionRepository(ZetAuctionDbContext context, IDateTimeProvider dateTimeProvider)
        : base(context, dateTimeProvider)
    {
    }

    public async Task<Auction?> GetByIdWithBidsAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await Db.Auctions
            .Include(a => a.Bids)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<IEnumerable<Auction>> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        return await Db.Auctions
            .AsNoTracking()
            .Where(a => a.Status == AuctionStatus.Active)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Auction>> GetByStatusAsync(
        AuctionStatus status, CancellationToken cancellationToken = default)
    {
        return await Db.Auctions
            .AsNoTracking()
            .Where(a => a.Status == status)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<Auction>> LockDueForFinalizationAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken = default)
    {
        // Postgres FOR UPDATE SKIP LOCKED: rows that another transaction
        // already locked are silently excluded from the result set, so
        // multiple finalisation workers running across replicas never see
        // overlapping batches. The outer transaction (managed by the caller
        // via UnitOfWork) holds the locks until commit/rollback.
        // Postgres SELECT * does NOT include system columns (xmin, ctid, ...).
        // EF Core wraps FromSqlRaw in a subquery and projects xmin as the
        // concurrency token, so we must surface it explicitly here or the
        // outer query fails with "column z.xmin does not exist".
        const string sql = @"
            SELECT *, xmin
            FROM ""Auctions""
            WHERE ""Status"" = 'Active'
              AND ""EndDate"" <= {0}
              AND ""IsDeleted"" = FALSE
            ORDER BY ""EndDate""
            LIMIT {1}
            FOR UPDATE SKIP LOCKED";

        // Two-step query: first claim auction rows under the lock, then
        // pull bids for those auctions. Doing it in one statement with a
        // join would multiply the locked row count and risk skipping
        // auctions that have many bids.
        var locked = await Db.Auctions
            .FromSqlRaw(sql, utcNow, batchSize)
            .ToListAsync(cancellationToken);

        if (locked.Count == 0)
        {
            return Array.Empty<Auction>();
        }

        var ids = locked.Select(a => a.Id).ToArray();
        await Db.Bids
            .Where(b => ids.Contains(b.AuctionId))
            .LoadAsync(cancellationToken);

        return locked;
    }
}
