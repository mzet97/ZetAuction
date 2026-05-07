using Paramore.Darker;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Bids.Queries;

public class GetBidHistoryQuery : IQuery<BaseResultList<BidViewModel>>
{
    public Guid TargetAuctionId { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
