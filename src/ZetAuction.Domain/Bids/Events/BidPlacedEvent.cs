using ZetAuction.Shared.Domain;

namespace ZetAuction.Domain.Bids.Events;

public sealed class BidPlacedEvent : DomainEvent
{
    public Guid AuctionId { get; init; }

    public Guid UserId { get; init; }

    public decimal Amount { get; init; }

    public decimal PreviousAmount { get; init; }

    public DateTime PlacedAtUtc { get; init; }
}
