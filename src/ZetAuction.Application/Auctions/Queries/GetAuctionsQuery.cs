using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionsQuery : IQuery<BaseResultList<AuctionViewModel>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
