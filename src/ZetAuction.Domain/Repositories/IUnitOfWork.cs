namespace ZetAuction.Domain.Repositories;

public interface IUnitOfWork : IDisposable
{
    IUserRepository Users { get; }
    IAuctionRepository Auctions { get; }
    IBidRepository Bids { get; }

    Task BeginTransactionAsync(CancellationToken cancellationToken = default);
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Detaches every tracked entity from the underlying ORM context. Used
    /// between retry attempts in handlers wrapped by a Polly resilience
    /// pipeline so a stale aggregate from a failed save does not poison the
    /// next attempt.
    /// </summary>
    Task ResetTrackingAsync();
}
