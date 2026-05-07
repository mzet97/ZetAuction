using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Users;

namespace ZetAuction.Infrastructure.Persistence;

public sealed class ZetAuctionDbContext : DbContext
{
    public ZetAuctionDbContext(DbContextOptions<ZetAuctionDbContext> options)
        : base(options)
    {
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Auction> Auctions => Set<Auction>();
    public DbSet<Bid> Bids => Set<Bid>();
    // The transactional outbox is owned by Brighter (Paramore.Brighter.Outbox.PostgreSql)
    // and lives in the "outbox_messages" table with Brighter's own schema.
    // It is not modelled as an EF entity — see DependencyInjection.cs and
    // DomainEventsSaveChangesInterceptor for the wiring.

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ZetAuctionDbContext).Assembly);

        modelBuilder.Entity<User>().HasQueryFilter(e => !e.IsDeleted);
        modelBuilder.Entity<Auction>().HasQueryFilter(e => !e.IsDeleted);
    }
}
