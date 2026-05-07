using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Commands;

public class CancelAuctionCommand : ResultCommand<BaseResult>
{
    public Guid AuctionId { get; set; }
    public string? Reason { get; set; }
}
