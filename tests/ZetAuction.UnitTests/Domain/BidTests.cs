using ZetAuction.Domain.Bids;

namespace ZetAuction.UnitTests.Domain;

public class BidTests
{
    [Fact]
    public void Constructor_Should_Initialize_All_Properties_When_Data_Is_Provided()
    {
        var auctionId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var placedAtUtc = TestHelpers.FixedUtcNow;

        var bid = new Bid(auctionId, userId, 150m, placedAtUtc);

        Assert.NotEqual(Guid.Empty, bid.Id);
        Assert.Equal(auctionId, bid.AuctionId);
        Assert.Equal(userId, bid.UserId);
        Assert.Equal(150m, bid.Amount);
        Assert.Equal(placedAtUtc, bid.PlacedAtUtc);
        Assert.True(bid.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_True_When_Bid_Is_Valid()
    {
        var bid = new Bid(Guid.NewGuid(), Guid.NewGuid(), 150m, TestHelpers.FixedUtcNow);

        Assert.True(bid.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_False_When_AuctionId_Is_Empty()
    {
        var bid = new Bid(Guid.Empty, Guid.NewGuid(), 150m, TestHelpers.FixedUtcNow);

        Assert.False(bid.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_False_When_UserId_Is_Empty()
    {
        var bid = new Bid(Guid.NewGuid(), Guid.Empty, 150m, TestHelpers.FixedUtcNow);

        Assert.False(bid.IsValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsValid_Should_Return_False_When_Amount_Is_Not_Positive(decimal amount)
    {
        var bid = new Bid(Guid.NewGuid(), Guid.NewGuid(), amount, TestHelpers.FixedUtcNow);

        Assert.False(bid.IsValid());
    }

    [Fact]
    public void Validate_Should_Return_Specific_Errors_When_Bid_Is_Invalid()
    {
        var bid = new Bid(Guid.Empty, Guid.Empty, 0m, TestHelpers.FixedUtcNow);

        var result = bid.Validate();
        var messages = result.Errors.Select(error => error.ErrorMessage).ToArray();

        Assert.False(result.IsValid);
        Assert.Contains("AuctionId is required.", messages);
        Assert.Contains("UserId is required.", messages);
        Assert.Contains("Bid amount must be greater than zero.", messages);
    }
}
