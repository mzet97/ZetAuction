using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Auctions;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionsByStatusQuery : IQuery<BaseResultList<AuctionViewModel>>
{
    public AuctionStatus AuctionStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
