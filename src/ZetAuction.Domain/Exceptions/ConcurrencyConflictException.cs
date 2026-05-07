namespace ZetAuction.Domain.Exceptions;

/// <summary>
/// Raised when an aggregate write loses an optimistic-concurrency check.
/// The infrastructure layer translates database-level conflicts (Postgres
/// xmin mismatch surfaced by EF Core as <c>DbUpdateConcurrencyException</c>)
/// into this domain-level exception so the application layer can react
/// without taking a direct dependency on EF Core.
/// </summary>
/// <remarks>
/// Handlers wrap the affected unit of work in a Polly retry pipeline that
/// reloads the aggregate, revalidates invariants and retries — typically
/// up to three attempts with exponential backoff and jitter — before
/// surfacing the conflict to the caller as an HTTP 409.
/// </remarks>
public sealed class ConcurrencyConflictException : DomainException
{
    public ConcurrencyConflictException(string aggregateName, object? aggregateId, Exception? inner = null)
        : base(
            inner is null
                ? $"Concurrency conflict on {aggregateName}{(aggregateId is null ? string.Empty : $" '{aggregateId}'")}."
                : $"Concurrency conflict on {aggregateName}{(aggregateId is null ? string.Empty : $" '{aggregateId}'")}: {inner.Message}",
            inner ?? new InvalidOperationException("Concurrency conflict"))
    {
        AggregateName = aggregateName;
        AggregateId = aggregateId;
    }

    public string AggregateName { get; }

    public object? AggregateId { get; }
}
