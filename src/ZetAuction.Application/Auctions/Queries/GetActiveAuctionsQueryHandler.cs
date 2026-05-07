using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetActiveAuctionsQueryHandler : QueryHandlerAsync<GetActiveAuctionsQuery, BaseResultList<AuctionViewModel>>
{
    private readonly IAuctionReadRepository _auctionReadRepository;
    private readonly ILogger<GetActiveAuctionsQueryHandler> _logger;

    public GetActiveAuctionsQueryHandler(IAuctionReadRepository auctionReadRepository, ILogger<GetActiveAuctionsQueryHandler> logger)
    {
        _auctionReadRepository = auctionReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResultList<AuctionViewModel>> ExecuteAsync(GetActiveAuctionsQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving active auctions page {Page} with size {PageSize}", query.Page, query.PageSize);

        var result = await _auctionReadRepository.ListActiveAsync(query.Page, query.PageSize, cancellationToken);

        var viewModels = result.Data!.Select(AuctionViewModelMapper.ToViewModel).ToList();
        return BaseResultList<AuctionViewModel>.Ok(viewModels, result.PagedResult);
    }
}
