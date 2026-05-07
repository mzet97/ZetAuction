using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Commands;

/// <summary>
/// Request payload to create a new auction. Field names match the assessment
/// specification (name, startingBid, endDateTime); the domain aggregate uses
/// its own canonical vocabulary (Title, StartingPrice, EndDate) and the
/// handler bridges the two.
/// </summary>
public class CreateAuctionCommand : ResultCommand<BaseResult<Guid>>
{
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal StartingBid { get; set; }
    public decimal MinBidIncrement { get; set; }
    public DateTime EndDateTime { get; set; }
    public Guid CreatedByUserId { get; set; }
}
