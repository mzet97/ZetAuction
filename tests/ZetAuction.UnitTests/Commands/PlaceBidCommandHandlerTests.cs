using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Bids.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class PlaceBidCommandHandlerTests
{
    private readonly Mock<IAuctionRepository> _auctionsMock = new();
    private readonly Mock<IBidRepository> _bidsMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly PlaceBidCommandHandler _handler;

    public PlaceBidCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(auctions: _auctionsMock, bids: _bidsMock);
        _handler = new PlaceBidCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<PlaceBidCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Place_Bid_Dispatch_Event_Update_Auction_And_Save_When_Bid_Is_Valid()
    {
        var auction = TestHelpers.CreateActiveAuction();
        var command = new PlaceBidCommand { AuctionId = auction.Id, UserId = Guid.NewGuid(), Amount = 150m };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal(150m, auction.CurrentPrice);
        Assert.Contains(auction.Bids, bid => bid.UserId == command.UserId && bid.Amount == command.Amount && bid.CreatedAtUtc == TestHelpers.FixedUtcNow);
        // Domain events stay on the aggregate in unit tests because the
        // SaveChangesInterceptor (which would drain them into the outbox in
        // a real persistence run) is not exercised by the in-memory mocks.
        var domainEvent = Assert.Single(auction.DomainEvents);
        var bidPlaced = Assert.IsType<BidPlacedEvent>(domainEvent);
        Assert.Equal(auction.Id, bidPlaced.AuctionId);
        Assert.Equal(command.UserId, bidPlaced.UserId);
        Assert.Equal(command.Amount, bidPlaced.Amount);
        _auctionsMock.Verify(repository => repository.UpdateAsync(auction), Times.Once);
        _bidsMock.Verify(repository => repository.AddAsync(It.IsAny<Bid>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Found()
    {
        var command = new PlaceBidCommand { AuctionId = Guid.NewGuid(), UserId = Guid.NewGuid(), Amount = 150m };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auction?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Auction not found.", result.Result.Message);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Active(AuctionStatus status)
    {
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
        var command = new PlaceBidCommand { AuctionId = auction.Id, UserId = Guid.NewGuid(), Amount = 150m };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Throw_InvalidBidException_When_Auction_Is_Expired()
    {
        var auction = TestHelpers.CreateActiveAuction(endDate: TestHelpers.FixedUtcNow.AddMinutes(-1));
        var command = new PlaceBidCommand { AuctionId = auction.Id, UserId = Guid.NewGuid(), Amount = 150m };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var exception = await Assert.ThrowsAsync<InvalidBidException>(() => _handler.HandleAsync(command));

        Assert.Contains("has ended", exception.Message);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    [InlineData(150)]
    public async Task HandleAsync_Should_Throw_InsufficientBidAmount_When_Amount_Is_Not_Greater_Than_CurrentPrice(decimal amount)
    {
        var auction = TestHelpers.CreateActiveAuction(currentPrice: 150m);
        var command = new PlaceBidCommand { AuctionId = auction.Id, UserId = Guid.NewGuid(), Amount = amount };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        await Assert.ThrowsAsync<InsufficientBidAmountException>(() => _handler.HandleAsync(command));

        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
