namespace ZetAuction.Application.Bids.ViewModels;

public class BidViewModel
{
    public Guid Id { get; set; }
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public DateTime PlacedAtUtc { get; set; }
    public string UserName { get; set; } = default!;
}
