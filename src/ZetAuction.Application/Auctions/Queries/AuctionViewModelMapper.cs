using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.Application.Auctions.Queries;

internal static class AuctionViewModelMapper
{
    public static AuctionViewModel ToViewModel(AuctionListItemDto auction)
    {
        return new AuctionViewModel
        {
            Id = auction.Id,
            Name = auction.Title,
            Description = auction.Description,
            StartingBid = auction.StartingPrice,
            MinBidIncrement = auction.MinBidIncrement,
            CurrentPrice = auction.CurrentPrice,
            HighestBid = auction.BidCount == 0 ? null : auction.CurrentPrice,
            EndDateTime = auction.EndDate,
            Status = auction.Status,
            CreatedByUserId = auction.CreatedByUserId,
            WinnerId = auction.WinnerId,
            WinningBidId = auction.WinningBidId,
            WinningAmount = auction.WinningAmount,
            FinalizedAtUtc = auction.FinalizedAtUtc,
            BidCount = auction.BidCount
        };
    }
}
