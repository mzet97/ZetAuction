using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Common.Observability;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Bids.Queries;

public class GetHighestBidQueryHandler : QueryHandlerAsync<GetHighestBidQuery, BaseResult<BidViewModel>>
{
    private readonly IBidReadRepository _bidReadRepository;
    private readonly ILogger<GetHighestBidQueryHandler> _logger;
    private readonly IBidCacheService? _bidCacheService;
    private readonly IAuctionMetrics _metrics;

    public GetHighestBidQueryHandler(IBidReadRepository bidReadRepository, ILogger<GetHighestBidQueryHandler> logger)
        : this(bidReadRepository, logger, null, NoopAuctionMetrics.Instance)
    {
    }

    public GetHighestBidQueryHandler(IBidReadRepository bidReadRepository, ILogger<GetHighestBidQueryHandler> logger, IBidCacheService? bidCacheService)
        : this(bidReadRepository, logger, bidCacheService, NoopAuctionMetrics.Instance)
    {
    }

    public GetHighestBidQueryHandler(
        IBidReadRepository bidReadRepository,
        ILogger<GetHighestBidQueryHandler> logger,
        IBidCacheService? bidCacheService,
        IAuctionMetrics? metrics)
    {
        _bidReadRepository = bidReadRepository;
        _logger = logger;
        _bidCacheService = bidCacheService;
        _metrics = metrics ?? NoopAuctionMetrics.Instance;
    }

    public override async Task<BaseResult<BidViewModel>> ExecuteAsync(GetHighestBidQuery query, CancellationToken cancellationToken = default)
    {
        if (_bidCacheService is not null)
        {
            var cachedBid = await _bidCacheService.GetHighestBidAsync(query.TargetAuctionId);
            if (cachedBid is not null)
            {
                _metrics.RecordCacheHit();
                return BaseResult<BidViewModel>.Ok(cachedBid);
            }

            _metrics.RecordCacheMiss();
        }

        var bid = await _bidReadRepository.GetHighestBidAsync(query.TargetAuctionId, cancellationToken);
        if (bid is null)
        {
            _logger.LogWarning("No bids found for auction {AuctionId}", query.TargetAuctionId);
            return BaseResult<BidViewModel>.Fail("No bids found for this auction.");
        }

        var viewModel = new BidViewModel
        {
            Id = bid.Id,
            AuctionId = bid.AuctionId,
            UserId = bid.UserId,
            Amount = bid.Amount,
            PlacedAtUtc = bid.PlacedAtUtc,
            UserName = bid.UserName
        };

        _logger.LogInformation("Highest bid retrieved for auction {AuctionId}: {Amount} by user {UserId}", query.TargetAuctionId, bid.Amount, bid.UserId);

        if (_bidCacheService is not null)
        {
            await _bidCacheService.SetHighestBidAsync(query.TargetAuctionId, viewModel);
        }

        return BaseResult<BidViewModel>.Ok(viewModel);
    }
}
