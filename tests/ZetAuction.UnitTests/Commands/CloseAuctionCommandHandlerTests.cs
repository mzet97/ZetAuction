using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class CloseAuctionCommandHandlerTests
{
    private readonly Mock<IAuctionRepository> _auctionsMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CloseAuctionCommandHandler _handler;

    public CloseAuctionCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(auctions: _auctionsMock);
        _handler = new CloseAuctionCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<CloseAuctionCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Close_Auction_With_Null_Winner_When_No_Bids_Exist()
    {
        var auction = TestHelpers.CreateActiveAuction();
        var command = new CloseAuctionCommand { AuctionId = auction.Id };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal(AuctionStatus.Finalized, auction.Status);
        Assert.Null(auction.WinnerId);
        Assert.Equal(TestHelpers.FixedUtcNow, auction.UpdatedAtUtc);
        var domainEvent = Assert.IsType<AuctionClosedEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Null(domainEvent.WinnerId);
        Assert.Null(domainEvent.WinningAmount);
        _auctionsMock.Verify(repository => repository.UpdateAsync(auction), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Close_Auction_And_Set_Winner_When_Bids_Exist()
    {
        var auction = TestHelpers.CreateActiveAuction();
        var winnerId = Guid.NewGuid();
        auction.PlaceBid(Guid.NewGuid(), 150m, TestHelpers.CreateDateTimeProvider().Object);
        auction.PlaceBid(winnerId, 250m, TestHelpers.CreateDateTimeProvider().Object);
        auction.ClearDomainEvents();
        var command = new CloseAuctionCommand { AuctionId = auction.Id };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal(AuctionStatus.Finalized, auction.Status);
        Assert.Equal(winnerId, auction.WinnerId);
        var domainEvent = Assert.IsType<AuctionClosedEvent>(Assert.Single(auction.DomainEvents));
        Assert.Equal(auction.Id, domainEvent.AuctionId);
        Assert.Equal(winnerId, domainEvent.WinnerId);
        Assert.Equal(250m, domainEvent.WinningAmount);
        _auctionsMock.Verify(repository => repository.UpdateAsync(auction), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Found()
    {
        var command = new CloseAuctionCommand { AuctionId = Guid.NewGuid() };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Auction?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Auction not found.", result.Result.Message);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuctionStatus.Draft)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public async Task HandleAsync_Should_Fail_When_Auction_Cannot_Be_Closed(AuctionStatus status)
    {
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
        var command = new CloseAuctionCommand { AuctionId = auction.Id };
        _auctionsMock.Setup(repository => repository.GetByIdWithBidsAsync(command.AuctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
