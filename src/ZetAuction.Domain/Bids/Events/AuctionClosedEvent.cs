using ZetAuction.Shared.Domain;

namespace ZetAuction.Domain.Bids.Events;

public sealed class AuctionClosedEvent : DomainEvent
{
    public Guid AuctionId { get; init; }

    public Guid? WinnerId { get; init; }

    public decimal? WinningAmount { get; init; }

    public DateTime ClosedAtUtc { get; init; }
}
