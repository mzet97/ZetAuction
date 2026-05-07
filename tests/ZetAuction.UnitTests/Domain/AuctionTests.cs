using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Exceptions;

namespace ZetAuction.UnitTests.Domain;

public class AuctionTests
{
    [Fact]
    public void Constructor_Should_Initialize_All_Properties_When_Data_Is_Provided()
    {
        var userId = Guid.NewGuid();
        var endDate = TestHelpers.FixedUtcNow.AddDays(1);

        var auction = new Auction("Auction", "Description", 100m, endDate, userId);

        Assert.NotEqual(Guid.Empty, auction.Id);
        Assert.Equal("Auction", auction.Title);
        Assert.Equal("Description", auction.Description);
        Assert.Equal(100m, auction.StartingPrice);
        Assert.Equal(100m, auction.CurrentPrice);
        Assert.Equal(endDate, auction.EndDate);
        Assert.Equal(AuctionStatus.Draft, auction.Status);
        Assert.Equal(userId, auction.CreatedByUserId);
        Assert.Null(auction.WinnerId);
        Assert.Empty(auction.Bids);
        Assert.Empty(auction.DomainEvents);
    }

    [Fact]
    public void Activate_Should_Set_Status_Active_When_Auction_Is_Draft()
    {
        var auction = CreateDraftAuction();

        auction.Activate();

        Assert.Equal(AuctionStatus.Active, auction.Status);
    }

    [Theory]
    [InlineData(AuctionStatus.Active)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public void Activate_Should_Throw_When_Auction_Is_Not_Draft(AuctionStatus status)
    {
        var auction = CreateDraftAuction();
        SetStatus(auction, status);

        var exception = Assert.Throws<DomainException>(auction.Activate);

        Assert.Contains("Only drafts can be activated", exception.Message);
    }

    [Fact]
    public void PlaceBid_Should_Add_Bid_Update_CurrentPrice_And_Add_Event_When_Bid_Is_Valid()
    {
        var auction = CreateActiveAuction();
        var userId = Guid.NewGuid();

        var bid = auction.PlaceBid(userId, 150m, TestHelpers.CreateDateTimeProvider().Object);

        Assert.Equal(150m, auction.CurrentPrice);
        Assert.Single(auction.Bids);
        Assert.Equal(bid.Id, auction.Bids[0].Id);
        Assert.Equal(auction.Id, bid.AuctionId);
        Assert.Equal(userId, bid.UserId);
        Assert.Equal(150m, bid.Amount);
        var domainEvent = Assert.IsType<BidPlacedEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Equal(userId, domainEvent.UserId);
        Assert.Equal(150m, domainEvent.Amount);
        Assert.Equal(100m, domainEvent.PreviousAmount);
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public void PlaceBid_Should_Throw_When_Auction_Is_Not_Active(AuctionStatus status)
    {
        var auction = CreateDraftAuction();
        SetStatus(auction, status);

        Assert.Throws<AuctionClosedException>(() => auction.PlaceBid(Guid.NewGuid(), 150m, TestHelpers.CreateDateTimeProvider().Object));
    }

    [Fact]
    public void PlaceBid_Should_Throw_When_Auction_Is_Expired()
    {
        var auction = CreateActiveAuction(endDate: TestHelpers.FixedUtcNow.AddMinutes(-1));

        var exception = Assert.Throws<InvalidBidException>(() => auction.PlaceBid(Guid.NewGuid(), 150m, TestHelpers.CreateDateTimeProvider().Object));

        Assert.Contains("has ended", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(99.99)]
    public void PlaceBid_Should_Throw_When_Amount_Is_Less_Than_CurrentPrice(decimal amount)
    {
        var auction = CreateActiveAuction();

        Assert.Throws<InsufficientBidAmountException>(() => auction.PlaceBid(Guid.NewGuid(), amount, TestHelpers.CreateDateTimeProvider().Object));
    }

    [Fact]
    public void PlaceBid_Should_Accept_First_Bid_When_Amount_Equals_StartingPrice()
    {
        var auction = CreateActiveAuction();

        var bid = auction.PlaceBid(Guid.NewGuid(), 100m, TestHelpers.CreateDateTimeProvider().Object);

        Assert.Equal(100m, bid.Amount);
        Assert.Equal(100m, auction.CurrentPrice);
    }

    [Fact]
    public void PlaceBid_Should_Require_MinBidIncrement_Above_CurrentPrice_After_First_Bid()
    {
        var auction = new Auction("Auction", "Description", 100m, 10m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        auction.Activate();

        auction.PlaceBid(Guid.NewGuid(), 100m, TestHelpers.CreateDateTimeProvider().Object);

        Assert.Throws<InsufficientBidAmountException>(() => auction.PlaceBid(Guid.NewGuid(), 109.99m, TestHelpers.CreateDateTimeProvider().Object));
        var bid = auction.PlaceBid(Guid.NewGuid(), 110m, TestHelpers.CreateDateTimeProvider().Object);

        Assert.Equal(110m, bid.Amount);
        Assert.Equal(110m, auction.CurrentPrice);
    }

    [Fact]
    public void PlaceBid_Should_Throw_When_Creator_Bids_On_Own_Auction()
    {
        var creatorId = Guid.NewGuid();
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), creatorId);
        auction.Activate();

        var exception = Assert.Throws<InvalidBidException>(() => auction.PlaceBid(creatorId, 150m, TestHelpers.CreateDateTimeProvider().Object));

        Assert.Contains("creator cannot bid", exception.Message);
    }

    [Fact]
    public void PlaceBid_Should_Update_CurrentPrice_To_Highest_Amount_When_Multiple_Bids_Are_Placed()
    {
        var auction = CreateActiveAuction();
        var provider = TestHelpers.CreateDateTimeProvider().Object;

        auction.PlaceBid(Guid.NewGuid(), 125m, provider);
        auction.PlaceBid(Guid.NewGuid(), 175m, provider);
        auction.PlaceBid(Guid.NewGuid(), 200m, provider);

        Assert.Equal(200m, auction.CurrentPrice);
        Assert.Equal(3, auction.Bids.Count);
        Assert.Equal(new[] { 125m, 175m, 200m }, auction.Bids.Select(bid => bid.Amount).ToArray());
    }

    [Fact]
    public void Close_Should_Close_Auction_Set_Winner_And_Add_Event_When_Bids_Exist()
    {
        var auction = CreateActiveAuction();
        var firstUserId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        auction.PlaceBid(firstUserId, 150m, TestHelpers.CreateDateTimeProvider().Object);
        auction.PlaceBid(winnerId, 250m, TestHelpers.CreateDateTimeProvider().Object);
        auction.ClearDomainEvents();

        auction.Close();

        Assert.Equal(AuctionStatus.Finalized, auction.Status);
        Assert.Equal(winnerId, auction.WinnerId);
        Assert.Equal(auction.Bids[^1].Id, auction.WinningBidId);
        Assert.Equal(250m, auction.WinningAmount);
        Assert.NotNull(auction.FinalizedAtUtc);
        Assert.Equal(250m, auction.CurrentPrice);
        var domainEvent = Assert.IsType<AuctionClosedEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Equal(winnerId, domainEvent.WinnerId);
        Assert.Equal(250m, domainEvent.WinningAmount);
    }

    [Fact]
    public void Close_Should_Close_Auction_With_Null_Winner_When_No_Bids_Exist()
    {
        var auction = CreateActiveAuction();

        auction.Close();

        Assert.Equal(AuctionStatus.Finalized, auction.Status);
        Assert.Null(auction.WinnerId);
        Assert.Null(auction.WinningBidId);
        Assert.Null(auction.WinningAmount);
        Assert.NotNull(auction.FinalizedAtUtc);
        Assert.Equal(100m, auction.CurrentPrice);
        var domainEvent = Assert.IsType<AuctionClosedEvent>(Assert.Single(auction.DomainEvents));
        Assert.Null(domainEvent.WinnerId);
        Assert.Null(domainEvent.WinningAmount);
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public void Close_Should_Throw_When_Auction_Is_Not_Active(AuctionStatus status)
    {
        var auction = CreateDraftAuction();
        SetStatus(auction, status);

        Assert.Throws<AuctionClosedException>(auction.Close);
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Active)]
    public void Cancel_Should_Set_Status_Cancelled_And_Add_Event_When_Status_Allows_Cancellation(AuctionStatus status)
    {
        var auction = CreateDraftAuction();
        SetStatus(auction, status);

        auction.Cancel("reason");

        Assert.Equal(AuctionStatus.Cancelled, auction.Status);
        var domainEvent = Assert.IsType<AuctionCancelledEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Equal("reason", domainEvent.Reason);
    }

    [Theory]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public void Cancel_Should_Throw_When_Status_Does_Not_Allow_Cancellation(AuctionStatus status)
    {
        var auction = CreateDraftAuction();
        SetStatus(auction, status);

        var exception = Assert.Throws<DomainException>(() => auction.Cancel("reason"));

        Assert.Contains($"Cannot cancel auction in status '{status}'", exception.Message);
    }

    [Fact]
    public void MarkWinner_Should_Set_Winner_And_CurrentPrice_When_Data_Is_Valid()
    {
        var auction = CreateActiveAuction();
        var winnerId = Guid.NewGuid();
        var winningBidId = Guid.NewGuid();

        auction.MarkWinner(winningBidId, winnerId, 300m);

        Assert.Equal(winnerId, auction.WinnerId);
        Assert.Equal(winningBidId, auction.WinningBidId);
        Assert.Equal(300m, auction.CurrentPrice);
    }

    [Fact]
    public void MarkWinner_Should_Throw_When_WinnerId_Is_Empty()
    {
        var auction = CreateActiveAuction();

        var exception = Assert.Throws<DomainException>(() => auction.MarkWinner(Guid.NewGuid(), Guid.Empty, 300m));

        Assert.Contains("WinnerId must be a valid non-empty GUID", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MarkWinner_Should_Throw_When_WinningAmount_Is_Not_Positive(decimal winningAmount)
    {
        var auction = CreateActiveAuction();

        var exception = Assert.Throws<DomainException>(() => auction.MarkWinner(Guid.NewGuid(), Guid.NewGuid(), winningAmount));

        Assert.Contains("Winning amount must be greater than zero", exception.Message);
    }

    [Fact]
    public void IsValid_Should_Return_True_When_Auction_Is_Valid()
    {
        var auction = CreateDraftAuction();

        Assert.True(auction.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsValid_Should_Return_False_When_Title_Is_Empty(string title)
    {
        var auction = new Auction(title, "Description", 100m, DateTime.UtcNow.AddDays(1), Guid.NewGuid());

        Assert.False(auction.IsValid());
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void IsValid_Should_Return_False_When_Description_Is_Empty(string description)
    {
        var auction = new Auction("Auction", description, 100m, DateTime.UtcNow.AddDays(1), Guid.NewGuid());

        Assert.False(auction.IsValid());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void IsValid_Should_Return_False_When_StartingPrice_Is_Not_Positive(decimal startingPrice)
    {
        var auction = new Auction("Auction", "Description", startingPrice, DateTime.UtcNow.AddDays(1), Guid.NewGuid());

        Assert.False(auction.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_False_When_EndDate_Is_In_The_Past()
    {
        var auction = new Auction("Auction", "Description", 100m, DateTime.UtcNow.AddDays(-1), Guid.NewGuid());

        Assert.False(auction.IsValid());
    }

    [Fact]
    public void IsValid_Should_Return_False_When_CreatedByUserId_Is_Empty()
    {
        var auction = new Auction("Auction", "Description", 100m, DateTime.UtcNow.AddDays(1), Guid.Empty);

        Assert.False(auction.IsValid());
    }

    [Fact]
    public void Validate_Should_Return_Specific_Errors_When_Auction_Is_Invalid()
    {
        var auction = new Auction("", "", 0m, DateTime.UtcNow.AddDays(-1), Guid.Empty);

        var result = auction.Validate();
        var messages = result.Errors.Select(error => error.ErrorMessage).ToArray();

        Assert.False(result.IsValid);
        Assert.Contains("Title is required.", messages);
        Assert.Contains("Description is required.", messages);
        Assert.Contains("Starting price must be greater than zero.", messages);
        Assert.Contains("End date must be in the future.", messages);
        Assert.Contains("CreatedByUserId is required.", messages);
    }

    private static Auction CreateDraftAuction(DateTime? endDate = null)
        => new("Auction", "Description", 100m, endDate ?? TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());

    private static Auction CreateActiveAuction(DateTime? endDate = null)
    {
        var auction = CreateDraftAuction(endDate);
        auction.Activate();
        return auction;
    }

    private static void SetStatus(Auction auction, AuctionStatus status)
    {
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
    }
}
