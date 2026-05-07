namespace ZetAuction.Application.Auctions.ViewModels;

/// <summary>
/// Public response shape for an auction. Field names match the assessment
/// specification: name, startingBid, endDateTime. The domain aggregate's
/// internal vocabulary (Title, StartingPrice, EndDate) is mapped to this
/// contract by <see cref="ZetAuction.Application.Common.MappingExtensions"/>.
/// </summary>
public class AuctionViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal StartingBid { get; set; }
    public decimal MinBidIncrement { get; set; }
    public decimal CurrentPrice { get; set; }
    public decimal? HighestBid { get; set; }
    public DateTime EndDateTime { get; set; }
    public string Status { get; set; } = default!;
    public Guid CreatedByUserId { get; set; }
    public Guid? WinnerId { get; set; }
    public Guid? WinningBidId { get; set; }
    public decimal? WinningAmount { get; set; }
    public DateTime? FinalizedAtUtc { get; set; }
    public int BidCount { get; set; }
}
