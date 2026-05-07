using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ZetAuction.Domain.Auctions;

namespace ZetAuction.Infrastructure.Persistence.Mappings;

internal sealed class AuctionMapping : IEntityTypeConfiguration<Auction>
{
    public void Configure(EntityTypeBuilder<Auction> builder)
    {
        builder.ToTable("Auctions");

        builder.HasKey(a => a.Id);

        builder.Property(a => a.Id)
            .ValueGeneratedNever();

        builder.Property(a => a.Title)
            .IsRequired()
            .HasMaxLength(300);

        builder.Property(a => a.Description)
            .IsRequired()
            .HasMaxLength(5000);

        builder.Property(a => a.StartingPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(a => a.MinBidIncrement)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(a => a.CurrentPrice)
            .IsRequired()
            .HasPrecision(18, 2);

        builder.Property(a => a.EndDate)
            .IsRequired();

        builder.Property(a => a.Status)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(20);

        builder.Property(a => a.CreatedByUserId)
            .IsRequired();

        builder.Property(a => a.WinnerId)
            .IsRequired(false);

        builder.Property(a => a.WinningBidId)
            .IsRequired(false);

        builder.Property(a => a.WinningAmount)
            .IsRequired(false)
            .HasPrecision(18, 2);

        builder.Property(a => a.FinalizedAtUtc)
            .IsRequired(false);

        builder.Property(a => a.CreatedAtUtc)
            .IsRequired();

        builder.Property(a => a.UpdatedAtUtc)
            .IsRequired();

        builder.Property(a => a.IsDeleted)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(a => a.DeletedAtUtc)
            .IsRequired(false);

        // Postgres optimistic concurrency: map RowVersion to the system
        // transaction id (xmin) so concurrent updates from multiple API
        // instances are rejected with DbUpdateConcurrencyException, which the
        // PlaceBidCommandHandler retry policy translates into a re-load and
        // re-validate against the latest aggregate state.
        builder.Property(a => a.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        builder.HasMany(a => a.Bids)
            .WithOne()
            .HasForeignKey(b => b.AuctionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(a => a.Status);

        builder.HasIndex(a => a.CreatedByUserId);

        builder.HasIndex(a => new { a.Status, a.EndDate })
            .HasDatabaseName("IX_Auctions_Status_EndDate");
    }
}
