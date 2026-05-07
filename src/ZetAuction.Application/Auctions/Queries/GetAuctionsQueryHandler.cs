using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auctions.Queries;

public class GetAuctionsQueryHandler : QueryHandlerAsync<GetAuctionsQuery, BaseResultList<AuctionViewModel>>
{
    private readonly IAuctionReadRepository _auctionReadRepository;
    private readonly ILogger<GetAuctionsQueryHandler> _logger;

    public GetAuctionsQueryHandler(IAuctionReadRepository auctionReadRepository, ILogger<GetAuctionsQueryHandler> logger)
    {
        _auctionReadRepository = auctionReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResultList<AuctionViewModel>> ExecuteAsync(GetAuctionsQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving all auctions page {Page} with size {PageSize}", query.Page, query.PageSize);

        var result = await _auctionReadRepository.ListAllAsync(query.Page, query.PageSize, cancellationToken);
        var viewModels = result.Data!.Select(AuctionViewModelMapper.ToViewModel).ToList();

        return BaseResultList<AuctionViewModel>.Ok(viewModels, result.PagedResult);
    }
}
