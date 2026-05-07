using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Commands;

public class CloseAuctionCommand : ResultCommand<BaseResult>
{
    public Guid AuctionId { get; set; }
}
