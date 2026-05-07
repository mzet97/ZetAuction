using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

public sealed class UserRepository : Repository<User>, IUserRepository
{
    public UserRepository(ZetAuctionDbContext context, IDateTimeProvider dateTimeProvider)
        : base(context, dateTimeProvider)
    {
    }

    public async Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await Db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);
    }

    public async Task<bool> ExistsByEmailAsync(string email, CancellationToken cancellationToken = default)
    {
        return await Db.Users
            .AsNoTracking()
            .AnyAsync(u => u.Email == email, cancellationToken);
    }

}
