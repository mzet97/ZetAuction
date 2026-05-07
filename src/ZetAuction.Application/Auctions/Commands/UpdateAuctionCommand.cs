using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Commands;

public class UpdateAuctionCommand : ResultCommand<BaseResult>
{
    public Guid AuctionId { get; set; }
    public string Title { get; set; } = default!;
    public string Description { get; set; } = default!;
    public DateTime EndDate { get; set; }
}
