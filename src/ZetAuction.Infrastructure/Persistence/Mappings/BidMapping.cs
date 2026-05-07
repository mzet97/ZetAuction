using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZetAuction.Domain.Bids;

namespace ZetAuction.Infrastructure.Persistence.Mappings;

internal sealed class BidMapping : IEntityTypeConfiguration<Bid>
{
    public void Configure(EntityTypeBuilder<Bid> builder)
    {
        builder.ToTable("Bids");

        builder.HasKey(b => b.Id);

        builder.Property(b => b.Id)
            .ValueGeneratedNever();

        builder.Property(b => b.AuctionId)
            .IsRequired();

        builder.Property(b => b.UserId)
            .IsRequired();

        builder.Property(b => b.Amount)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(b => b.PlacedAtUtc)
            .IsRequired();

        builder.Property(b => b.CreatedAtUtc)
            .IsRequired();

        builder.Property(b => b.UpdatedAtUtc)
            .IsRequired();

        builder.HasIndex(b => b.AuctionId);

        builder.HasIndex(b => b.UserId);

        builder.HasIndex(b => new { b.AuctionId, b.Amount });
    }
}
