using ZetAuction.Domain.Auctions;

namespace ZetAuction.Domain.Repositories;

public interface IAuctionRepository : IRepository<Auction>
{
    Task<Auction?> GetByIdWithBidsAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IEnumerable<Auction>> GetActiveAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<Auction>> GetByStatusAsync(AuctionStatus status, CancellationToken cancellationToken = default);

    /// <summary>
    /// Atomically locks up to <paramref name="batchSize"/> auctions whose
    /// <c>EndDate</c> has elapsed and whose status is still <c>Active</c>,
    /// returning them with their <c>Bids</c> collection eagerly loaded.
    /// Implementations must use <c>FOR UPDATE SKIP LOCKED</c> so multiple
    /// finalisation workers can run concurrently across API replicas without
    /// processing the same auction twice. The caller is responsible for the
    /// surrounding transaction; the rows are released when it commits or
    /// rolls back.
    /// </summary>
    Task<IReadOnlyList<Auction>> LockDueForFinalizationAsync(
        DateTime utcNow,
        int batchSize,
        CancellationToken cancellationToken = default);
}
