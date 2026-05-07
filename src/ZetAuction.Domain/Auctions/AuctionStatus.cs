namespace ZetAuction.Domain.Auctions;

/// <summary>
/// Lifecycle states an <see cref="Auction"/> moves through.
/// </summary>
/// <remarks>
/// The legacy <c>Closed</c> value was removed in Phase 1 of the Principal SWE
/// remediation plan. <see cref="Finalized"/> is the single terminal state for
/// a completed auction; <see cref="Cancelled"/> for a withdrawn one.
/// </remarks>
public enum AuctionStatus
{
    Draft = 0,
    Active = 1,
    Finalized = 2,
    Cancelled = 3
}
