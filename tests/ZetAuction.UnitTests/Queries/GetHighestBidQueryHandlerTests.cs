using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Bids.Queries;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Queries;

public class GetHighestBidQueryHandlerTests
{
    private readonly Mock<IBidReadRepository> _repositoryMock = new();
    private readonly GetHighestBidQueryHandler _handler;

    public GetHighestBidQueryHandlerTests()
    {
        _handler = new GetHighestBidQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetHighestBidQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Highest_Bid_When_Bid_Exists()
    {
        var auctionId = Guid.NewGuid();
        var bid = new BidHistoryDto(Guid.NewGuid(), auctionId, Guid.NewGuid(), 250m, TestHelpers.FixedUtcNow, "Bidder");
        _repositoryMock.Setup(repository => repository.GetHighestBidAsync(auctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bid);

        var result = await _handler.ExecuteAsync(new GetHighestBidQuery { TargetAuctionId = auctionId });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(bid.Id, result.Data.Id);
        Assert.Equal(auctionId, result.Data.AuctionId);
        Assert.Equal(250m, result.Data.Amount);
        Assert.Equal("Bidder", result.Data.UserName);
        _repositoryMock.Verify(repository => repository.GetHighestBidAsync(auctionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_No_Bid_Exists()
    {
        var auctionId = Guid.NewGuid();
        _repositoryMock.Setup(repository => repository.GetHighestBidAsync(auctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BidHistoryDto?)null);

        var result = await _handler.ExecuteAsync(new GetHighestBidQuery { TargetAuctionId = auctionId });

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("No bids found for this auction.", result.Message);
        _repositoryMock.Verify(repository => repository.GetHighestBidAsync(auctionId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
