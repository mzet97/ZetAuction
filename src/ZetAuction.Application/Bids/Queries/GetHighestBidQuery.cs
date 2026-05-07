using Paramore.Darker;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Bids.Queries;

public class GetHighestBidQuery : IQuery<BaseResult<BidViewModel>>
{
    public Guid TargetAuctionId { get; set; }
}
