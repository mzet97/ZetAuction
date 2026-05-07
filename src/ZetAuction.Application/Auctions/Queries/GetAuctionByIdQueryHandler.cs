using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionByIdQueryHandler : QueryHandlerAsync<GetAuctionByIdQuery, BaseResult<AuctionViewModel>>
{
    private readonly IAuctionReadRepository _auctionReadRepository;
    private readonly ILogger<GetAuctionByIdQueryHandler> _logger;

    public GetAuctionByIdQueryHandler(IAuctionReadRepository auctionReadRepository, ILogger<GetAuctionByIdQueryHandler> logger)
    {
        _auctionReadRepository = auctionReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResult<AuctionViewModel>> ExecuteAsync(GetAuctionByIdQuery query, CancellationToken cancellationToken = default)
    {
        var auction = await _auctionReadRepository.GetDetailsByIdAsync(query.AuctionId, cancellationToken);
        if (auction is null)
        {
            _logger.LogWarning("Auction {AuctionId} not found", query.AuctionId);
            return BaseResult<AuctionViewModel>.Fail("Auction not found.");
        }

        _logger.LogInformation("Auction {AuctionId} retrieved successfully", query.AuctionId);

        var viewModel = new AuctionViewModel
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
        return BaseResult<AuctionViewModel>.Ok(viewModel);
    }
}
