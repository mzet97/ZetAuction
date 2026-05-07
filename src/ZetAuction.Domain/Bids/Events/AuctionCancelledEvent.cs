using ZetAuction.Shared.Domain;

namespace ZetAuction.Domain.Bids.Events;

public sealed class AuctionCancelledEvent : DomainEvent
{
    public Guid AuctionId { get; init; }

    public DateTime CancelledAtUtc { get; init; }

    public string? Reason { get; init; }
}
