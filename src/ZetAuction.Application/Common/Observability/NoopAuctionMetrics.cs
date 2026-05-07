namespace ZetAuction.Application.Common.Observability;

/// <summary>
/// No-op implementation used by tests and by handler ctors that don't
/// receive a real <see cref="IAuctionMetrics"/>. Keeps the production
/// code path free of <c>null</c> checks while still letting unit tests
/// instantiate handlers without standing up the OpenTelemetry stack.
/// </summary>
public sealed class NoopAuctionMetrics : IAuctionMetrics
{
    public static readonly NoopAuctionMetrics Instance = new();

    private NoopAuctionMetrics() { }

    public void RecordBidPlaced() { }
    public void RecordBidRejected(string reason) { }
    public void RecordAuctionFinalized() { }
    public void RecordRateLimitHit() { }
    public void RecordConcurrencyConflict() { }
    public void RecordCacheHit() { }
    public void RecordCacheMiss() { }
    public void RecordOutboxDispatched() { }
    public void RecordOutboxFailed() { }
    public void RecordOutboxDuplicateSkipped() { }
    public void RecordOutboxDeadLettered() { }
    public void RecordBidPlacementDuration(double milliseconds) { }
}
