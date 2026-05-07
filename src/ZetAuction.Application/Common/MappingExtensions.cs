using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Users;

namespace ZetAuction.Application.Common;

public static class MappingExtensions
{
    public static UserViewModel ToViewModel(this User user)
    {
        return new UserViewModel
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role.ToString(),
            CreatedAtUtc = user.CreatedAtUtc
        };
    }

    public static AuctionViewModel ToViewModel(this Auction auction)
    {
        return new AuctionViewModel
        {
            Id = auction.Id,
            Name = auction.Title,
            Description = auction.Description,
            StartingBid = auction.StartingPrice,
            MinBidIncrement = auction.MinBidIncrement,
            CurrentPrice = auction.CurrentPrice,
            HighestBid = auction.HighestBid,
            EndDateTime = auction.EndDate,
            Status = auction.Status.ToString(),
            CreatedByUserId = auction.CreatedByUserId,
            WinnerId = auction.WinnerId,
            WinningBidId = auction.WinningBidId,
            WinningAmount = auction.WinningAmount,
            FinalizedAtUtc = auction.FinalizedAtUtc,
            BidCount = auction.Bids.Count
        };
    }

    public static BidViewModel ToViewModel(this Bid bid, string userName)
    {
        return new BidViewModel
        {
            Id = bid.Id,
            AuctionId = bid.AuctionId,
            UserId = bid.UserId,
            Amount = bid.Amount,
            PlacedAtUtc = bid.PlacedAtUtc,
            UserName = userName
        };
    }
}
