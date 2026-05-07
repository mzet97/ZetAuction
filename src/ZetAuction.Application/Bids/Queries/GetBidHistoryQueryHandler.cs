using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Bids.Queries;

public class GetBidHistoryQueryHandler : QueryHandlerAsync<GetBidHistoryQuery, BaseResultList<BidViewModel>>
{
    private readonly IBidReadRepository _bidReadRepository;
    private readonly ILogger<GetBidHistoryQueryHandler> _logger;

    public GetBidHistoryQueryHandler(IBidReadRepository bidReadRepository, ILogger<GetBidHistoryQueryHandler> logger)
    {
        _bidReadRepository = bidReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResultList<BidViewModel>> ExecuteAsync(GetBidHistoryQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving bid history for auction {AuctionId} page {Page} with size {PageSize}", query.TargetAuctionId, query.Page, query.PageSize);

        var result = await _bidReadRepository.GetBidHistoryAsync(query.TargetAuctionId, query.Page, query.PageSize, cancellationToken);

        var viewModels = result.Data!.Select(b => new BidViewModel
        {
            Id = b.Id,
            AuctionId = b.AuctionId,
            UserId = b.UserId,
            Amount = b.Amount,
            PlacedAtUtc = b.PlacedAtUtc,
            UserName = b.UserName
        }).ToList();

        return BaseResultList<BidViewModel>.Ok(viewModels, result.PagedResult);
    }
}
