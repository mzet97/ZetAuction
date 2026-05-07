using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Exceptions;

namespace ZetAuction.UnitTests.Domain;

/// <summary>
/// Property-style tests for the Auction aggregate. The fact-based
/// tests in <see cref="AuctionTests"/> pin behaviour for hand-picked
/// cases; these drive the aggregate with seeded pseudo-random input
/// streams to surface invariants that should hold for *any*
/// admissible sequence of bids: monotonicity of CurrentPrice,
/// one-event-per-accepted-bid, "highest amount wins" closure
/// semantics, and floor/creator rejection rules.
/// </summary>
/// <remarks>
/// The seeds are <see cref="Theory"/> arguments so when an invariant
/// regresses, the seed reproducing the failure is part of the test
/// name and survives flaky-test triage. We do not need a full
/// QuickCheck-style shrinker for these properties — the input shape
/// is small and the failure mode is usually obvious from the seed.
/// </remarks>
public class AuctionPropertyTests
{
    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(42)]
    [InlineData(101)]
    public void CurrentPrice_Is_Monotonically_Non_Decreasing(int seed)
    {
        var random = new Random(seed);
        var auction = NewAuction();
        var dateTimeProvider = TestHelpers.CreateDateTimeProvider().Object;

        decimal previous = auction.CurrentPrice;
        for (var i = 0; i < 50; i++)
        {
            var amount = auction.CurrentPrice + random.Next(1, 100);
            auction.PlaceBid(Guid.NewGuid(), amount, dateTimeProvider);
            Assert.True(auction.CurrentPrice >= previous,
                $"Seed {seed}, iteration {i}: CurrentPrice regressed from {previous} to {auction.CurrentPrice}.");
            previous = auction.CurrentPrice;
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(42)]
    [InlineData(101)]
    public void Every_Accepted_Bid_Emits_Exactly_One_BidPlacedEvent(int seed)
    {
        var random = new Random(seed);
        var auction = NewAuction();
        var dateTimeProvider = TestHelpers.CreateDateTimeProvider().Object;

        var accepted = 0;
        for (var i = 0; i < 30; i++)
        {
            var amount = auction.CurrentPrice + random.Next(1, 50);
            auction.PlaceBid(Guid.NewGuid(), amount, dateTimeProvider);
            accepted++;
        }

        var emitted = auction.DomainEvents.OfType<BidPlacedEvent>().Count();
        Assert.Equal(accepted, emitted);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(42)]
    [InlineData(101)]
    public void Close_Picks_The_Highest_Amount_As_Winner(int seed)
    {
        var random = new Random(seed);
        var auction = NewAuction();
        var dateTimeProvider = TestHelpers.CreateDateTimeProvider().Object;

        decimal max = auction.CurrentPrice;
        for (var i = 0; i < 25; i++)
        {
            var amount = auction.CurrentPrice + random.Next(1, 75);
            auction.PlaceBid(Guid.NewGuid(), amount, dateTimeProvider);
            if (amount > max) max = amount;
        }

        auction.Close(TestHelpers.FixedUtcNow);

        Assert.Equal(max, auction.WinningAmount);
        Assert.Equal(AuctionStatus.Finalized, auction.Status);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(42)]
    [InlineData(101)]
    public void Bids_Below_Floor_Are_Rejected_With_InsufficientBidAmount(int seed)
    {
        var random = new Random(seed);
        var auction = NewAuction();
        var dateTimeProvider = TestHelpers.CreateDateTimeProvider().Object;

        // Place a strictly-higher first bid so the floor advances.
        var firstAmount = auction.CurrentPrice + random.Next(1, 100);
        auction.PlaceBid(Guid.NewGuid(), firstAmount, dateTimeProvider);

        // Anything strictly below CurrentPrice + MinBidIncrement must
        // be rejected.
        var lower = Math.Max(1m, auction.CurrentPrice - random.Next(1, 50));

        Assert.Throws<InsufficientBidAmountException>(() =>
            auction.PlaceBid(Guid.NewGuid(), lower, dateTimeProvider));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(13)]
    [InlineData(42)]
    [InlineData(101)]
    public void Auction_Creator_Cannot_Bid_On_Their_Own_Auction(int seed)
    {
        var random = new Random(seed);
        var creatorId = Guid.NewGuid();
        var auction = new Auction(
            "Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), creatorId);
        auction.MarkCreated(TestHelpers.FixedUtcNow);
        auction.Activate();

        var dateTimeProvider = TestHelpers.CreateDateTimeProvider().Object;
        var amount = auction.CurrentPrice + random.Next(1, 100);

        Assert.Throws<InvalidBidException>(() =>
            auction.PlaceBid(creatorId, amount, dateTimeProvider));
    }

    private static Auction NewAuction()
    {
        var auction = new Auction(
            "Auction",
            "Description",
            startingPrice: 100m,
            minBidIncrement: 1m,
            endDate: TestHelpers.FixedUtcNow.AddDays(1),
            createdByUserId: Guid.NewGuid());
        auction.MarkCreated(TestHelpers.FixedUtcNow);
        auction.Activate();
        return auction;
    }
}
