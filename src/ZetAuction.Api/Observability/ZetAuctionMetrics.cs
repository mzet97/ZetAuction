using System.Diagnostics.Metrics;
using ZetAuction.Application.Common.Observability;

namespace ZetAuction.Api.Observability;

/// <summary>
/// Business and infrastructure metrics emitted via the System.Diagnostics
/// Meter API and exported through the Prometheus and OTLP pipelines wired
/// in <see cref="ObservabilityConfig"/>. Every counter/histogram has a
/// stable name so SLO dashboards and alerting rules survive code
/// re-organisations.
/// </summary>
/// <remarks>
/// Naming follows the OTEL semantic conventions where applicable
/// (snake_case, dot-segmented). The meter name <c>ZetAuction.Api</c>
/// is what dashboards filter on.
/// </remarks>
public sealed class ZetAuctionMetrics : IAuctionMetrics
{
    public const string MeterName = "ZetAuction.Api";

    private readonly Meter _meter;
    private readonly Counter<long> _bidsPlaced;
    private readonly Counter<long> _bidsRejected;
    private readonly Counter<long> _auctionsFinalized;
    private readonly Counter<long> _rateLimitHits;
    private readonly Counter<long> _concurrencyConflicts;
    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _outboxDispatched;
    private readonly Counter<long> _outboxFailed;
    private readonly Counter<long> _outboxDuplicateSkipped;
    private readonly Counter<long> _outboxDeadLettered;
    private readonly Histogram<double> _bidPlacementDuration;

    public ZetAuctionMetrics(IMeterFactory meterFactory)
    {
        _meter = meterFactory.Create(MeterName);

        _bidsPlaced = _meter.CreateCounter<long>(
            "zetauction.bids.placed",
            unit: "{bid}",
            description: "Number of bids accepted and persisted.");

        _bidsRejected = _meter.CreateCounter<long>(
            "zetauction.bids.rejected",
            unit: "{bid}",
            description: "Number of bids rejected by validation, status, or concurrency rules.");

        _auctionsFinalized = _meter.CreateCounter<long>(
            "zetauction.auctions.finalized",
            unit: "{auction}",
            description: "Number of auctions transitioned to the Finalized state by the worker.");

        _rateLimitHits = _meter.CreateCounter<long>(
            "zetauction.rate_limit.hits",
            unit: "{request}",
            description: "Number of requests rejected by the bid rate limiter.");

        _concurrencyConflicts = _meter.CreateCounter<long>(
            "zetauction.db.concurrency_conflicts",
            unit: "{conflict}",
            description: "Number of optimistic-concurrency conflicts surfaced by Postgres xmin.");

        _cacheHits = _meter.CreateCounter<long>(
            "zetauction.cache.hits",
            unit: "{request}",
            description: "Number of highest-bid cache reads served from Redis.");

        _cacheMisses = _meter.CreateCounter<long>(
            "zetauction.cache.misses",
            unit: "{request}",
            description: "Number of highest-bid cache reads that fell through to Postgres.");

        _outboxDispatched = _meter.CreateCounter<long>(
            "zetauction.outbox.dispatched",
            unit: "{message}",
            description: "Number of outbox messages successfully published.");

        _outboxFailed = _meter.CreateCounter<long>(
            "zetauction.outbox.failed",
            unit: "{message}",
            description: "Number of outbox messages that failed publication and entered backoff.");

        _outboxDuplicateSkipped = _meter.CreateCounter<long>(
            "zetauction.outbox.duplicate_skipped",
            unit: "{message}",
            description: "Number of outbox messages skipped because another replica already claimed them via processed_events.");

        _outboxDeadLettered = _meter.CreateCounter<long>(
            "zetauction.outbox.dead_lettered",
            unit: "{message}",
            description: "Number of outbox messages forced to dispatched after exceeding the per-message retry threshold. Should alert on first non-zero value.");

        _bidPlacementDuration = _meter.CreateHistogram<double>(
            "zetauction.bid.placement.duration",
            unit: "ms",
            description: "End-to-end latency of accepted bid placements.");
    }

    public void RecordBidPlaced() => _bidsPlaced.Add(1);

    public void RecordBidRejected(string reason) =>
        _bidsRejected.Add(1, new KeyValuePair<string, object?>("reason", reason));

    public void RecordAuctionFinalized() => _auctionsFinalized.Add(1);

    public void RecordRateLimitHit() => _rateLimitHits.Add(1);

    public void RecordConcurrencyConflict() => _concurrencyConflicts.Add(1);

    public void RecordCacheHit() => _cacheHits.Add(1);

    public void RecordCacheMiss() => _cacheMisses.Add(1);

    public void RecordOutboxDispatched() => _outboxDispatched.Add(1);

    public void RecordOutboxFailed() => _outboxFailed.Add(1);

    public void RecordOutboxDuplicateSkipped() => _outboxDuplicateSkipped.Add(1);

    public void RecordOutboxDeadLettered() => _outboxDeadLettered.Add(1);

    public void RecordBidPlacementDuration(double milliseconds) =>
        _bidPlacementDuration.Record(milliseconds);
}
