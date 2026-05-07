using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionByIdQuery : IQuery<BaseResult<AuctionViewModel>>
{
    public Guid AuctionId { get; set; }
}
