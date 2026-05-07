using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class CancelAuctionCommandHandlerTests
{
    private readonly Mock<IAuctionRepository> _auctionsMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CancelAuctionCommandHandler _handler;

    public CancelAuctionCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(auctions: _auctionsMock);
        _handler = new CancelAuctionCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<CancelAuctionCommandHandler>>());
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Active)]
    public async Task HandleAsync_Should_Cancel_Auction_Dispatch_Event_Update_And_Save_When_Status_Allows_Cancellation(AuctionStatus status)
    {
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
        var command = new CancelAuctionCommand { AuctionId = auction.Id, Reason = "test cancellation" };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal(AuctionStatus.Cancelled, auction.Status);
        Assert.Equal(TestHelpers.FixedUtcNow, auction.UpdatedAtUtc);
        var domainEvent = Assert.IsType<AuctionCancelledEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Equal(command.Reason, domainEvent.Reason);
        _auctionsMock.Verify(repository => repository.UpdateAsync(auction), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Found()
    {
        var command = new CancelAuctionCommand { AuctionId = Guid.NewGuid(), Reason = "missing" };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync((Auction?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Auction not found.", result.Result.Message);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public async Task HandleAsync_Should_Fail_When_Auction_Cannot_Be_Cancelled(AuctionStatus status)
    {
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
        var command = new CancelAuctionCommand { AuctionId = auction.Id, Reason = "reason" };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
