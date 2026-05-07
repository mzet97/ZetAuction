using ZetAuction.Shared.Responses;

namespace ZetAuction.Domain.Repositories;

public interface IUserReadRepository
{
    Task<UserReadDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<BaseResultList<UserReadDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default);
}
