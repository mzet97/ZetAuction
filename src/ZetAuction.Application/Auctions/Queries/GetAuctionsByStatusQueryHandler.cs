using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionsByStatusQueryHandler : QueryHandlerAsync<GetAuctionsByStatusQuery, BaseResultList<AuctionViewModel>>
{
    private readonly IAuctionReadRepository _auctionReadRepository;
    private readonly ILogger<GetAuctionsByStatusQueryHandler> _logger;

    public GetAuctionsByStatusQueryHandler(IAuctionReadRepository auctionReadRepository, ILogger<GetAuctionsByStatusQueryHandler> logger)
    {
        _auctionReadRepository = auctionReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResultList<AuctionViewModel>> ExecuteAsync(GetAuctionsByStatusQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving auctions with status {Status} page {Page} with size {PageSize}", query.AuctionStatus, query.Page, query.PageSize);

        var result = await _auctionReadRepository.ListByStatusAsync(query.AuctionStatus, query.Page, query.PageSize, cancellationToken);

        var viewModels = result.Data!.Select(AuctionViewModelMapper.ToViewModel).ToList();
        return BaseResultList<AuctionViewModel>.Ok(viewModels, result.PagedResult);
    }
}
