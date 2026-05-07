using Paramore.Brighter;

namespace ZetAuction.Shared.Domain;

public abstract class DomainEvent : Event, IDomainEvent
{
    // Brighter v9 Event constructor takes a Guid (v10 introduced its own
    // Id struct exposed via Id.Random() — that surface no longer exists in
    // v9). We assign the same Guid we expose as EventId so the Brighter
    // Message identifier and our domain event identifier line up in logs
    // and the outbox.
    protected DomainEvent() : base(Guid.NewGuid())
    {
        EventId = base.Id;
    }

    public Guid EventId { get; init; }

    public DateTime OccurredOnUtc { get; init; } = DateTime.UtcNow;
}
