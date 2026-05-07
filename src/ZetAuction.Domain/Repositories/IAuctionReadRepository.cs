using ZetAuction.Domain.Auctions;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Domain.Repositories;

public interface IAuctionReadRepository
{
    Task<AuctionDetailsDto?> GetDetailsByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseResultList<AuctionListItemDto>> ListAllAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BaseResultList<AuctionListItemDto>> ListActiveAsync(int page, int pageSize, CancellationToken cancellationToken = default);
    Task<BaseResultList<AuctionListItemDto>> ListByStatusAsync(AuctionStatus status, int page, int pageSize, CancellationToken cancellationToken = default);
}
