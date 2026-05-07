using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Bids.Commands;

public class PlaceBidCommand : ResultCommand<BaseResult>
{
    public Guid AuctionId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
}
