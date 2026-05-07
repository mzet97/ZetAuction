namespace ZetAuction.Application.Common.Observability;

/// <summary>
/// Application-layer abstraction over the OpenTelemetry meters owned
/// by the API host. Handlers depend on this interface so they can emit
/// business metrics (bids placed/rejected, concurrency conflicts, cache
/// hits, etc.) without taking a hard dependency on the API project or
/// on <see cref="System.Diagnostics.Metrics.Meter"/>. The concrete
/// implementation lives in <c>ZetAuction.Api.Observability</c>.
/// </summary>
public interface IAuctionMetrics
{
    void RecordBidPlaced();
    void RecordBidRejected(string reason);
    void RecordAuctionFinalized();
    void RecordRateLimitHit();
    void RecordConcurrencyConflict();
    void RecordCacheHit();
    void RecordCacheMiss();
    void RecordOutboxDispatched();
    void RecordOutboxFailed();

    /// <summary>
    /// A second replica beat us to the cross-replica claim on
    /// <c>processed_events</c>; we skipped <c>PublishAsync</c> but still
    /// marked the row dispatched. Useful as a "how often are we racing?"
    /// signal — sustained non-zero rate suggests either over-replication
    /// or a poll interval too short for the dispatch latency.
    /// </summary>
    void RecordOutboxDuplicateSkipped();

    /// <summary>
    /// A message exceeded the dispatcher retry threshold and was
    /// dead-lettered (forced to <c>Dispatched</c> with the payload kept
    /// on the row for forensic triage). Should alert on first non-zero
    /// reading — see runbooks/outbox-stuck-messages.md.
    /// </summary>
    void RecordOutboxDeadLettered();

    void RecordBidPlacementDuration(double milliseconds);
}
