using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class UpdateAuctionCommandHandlerTests
{
    private readonly Mock<IAuctionRepository> _auctionsMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly UpdateAuctionCommandHandler _handler;

    public UpdateAuctionCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(auctions: _auctionsMock);
        _handler = new UpdateAuctionCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<UpdateAuctionCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Update_Auction_When_Auction_Is_Draft()
    {
        var auction = new Auction("Draft", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        var command = new UpdateAuctionCommand { AuctionId = auction.Id, Title = "Updated", Description = "Updated description", EndDate = TestHelpers.FixedUtcNow.AddDays(2) };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.UpdateAsync(auction), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Found()
    {
        var command = new UpdateAuctionCommand { AuctionId = Guid.NewGuid(), Title = "Updated", Description = "Updated description", EndDate = TestHelpers.FixedUtcNow.AddDays(2) };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync((Auction?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Auction not found.", result.Result.Message);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData(AuctionStatus.Active)]
    [InlineData(AuctionStatus.Finalized)]
    [InlineData(AuctionStatus.Cancelled)]
    public async Task HandleAsync_Should_Fail_When_Auction_Is_Not_Draft(AuctionStatus status)
    {
        var auction = new Auction("Auction", "Description", 100m, TestHelpers.FixedUtcNow.AddDays(1), Guid.NewGuid());
        typeof(Auction).GetProperty(nameof(Auction.Status))!.SetValue(auction, status);
        var command = new UpdateAuctionCommand { AuctionId = auction.Id, Title = "Updated", Description = "Updated description", EndDate = TestHelpers.FixedUtcNow.AddDays(2) };
        _auctionsMock.Setup(repository => repository.GetByIdAsync(command.AuctionId)).ReturnsAsync(auction);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Only draft auctions can be updated.", result.Result.Message);
        _auctionsMock.Verify(repository => repository.UpdateAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
