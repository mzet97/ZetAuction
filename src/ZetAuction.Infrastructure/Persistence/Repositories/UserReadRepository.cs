using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class UserReadRepository : IUserReadRepository
{
    private readonly ZetAuctionDbContext _context;

    public UserReadRepository(ZetAuctionDbContext context)
    {
        _context = context;
    }

    public async Task<UserReadDto?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AsNoTracking()
            .Where(u => u.Id == id)
            .Select(u => new UserReadDto(
                u.Id,
                u.Name,
                u.Email,
                u.Role.ToString(),
                u.CreatedAtUtc))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<BaseResultList<UserReadDto>> ListAsync(int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));

        var query = _context.Users.AsNoTracking();
        var totalCount = await query.CountAsync(cancellationToken);
        var pagedResult = PagedResult.Create(page, pageSize, totalCount);

        var users = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip(pagedResult.Skip())
            .Take(pageSize)
            .Select(u => new UserReadDto(
                u.Id,
                u.Name,
                u.Email,
                u.Role.ToString(),
                u.CreatedAtUtc))
            .ToListAsync(cancellationToken);

        return BaseResultList<UserReadDto>.Ok(users, pagedResult);
    }
}
