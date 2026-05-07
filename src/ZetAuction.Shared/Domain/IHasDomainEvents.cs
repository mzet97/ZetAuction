namespace ZetAuction.Shared.Domain;

/// <summary>
/// Marker contract implemented by aggregates that raise domain events.
/// The persistence layer uses it to discover events to enqueue into the
/// transactional outbox without having to reflect on every entity type.
/// </summary>
public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }
    void ClearDomainEvents();
}
