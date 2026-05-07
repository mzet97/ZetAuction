using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Bids.Queries;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.UnitTests.Queries;

public class GetBidHistoryQueryHandlerTests
{
    private readonly Mock<IBidReadRepository> _repositoryMock = new();
    private readonly GetBidHistoryQueryHandler _handler;

    public GetBidHistoryQueryHandlerTests()
    {
        _handler = new GetBidHistoryQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetBidHistoryQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Bid_History_When_Bids_Exist()
    {
        var auctionId = Guid.NewGuid();
        var bids = new List<BidHistoryDto>
        {
            new(Guid.NewGuid(), auctionId, Guid.NewGuid(), 150m, TestHelpers.FixedUtcNow, "First Bidder"),
            new(Guid.NewGuid(), auctionId, Guid.NewGuid(), 175m, TestHelpers.FixedUtcNow.AddMinutes(1), "Second Bidder")
        };
        var paged = PagedResult.Create(1, 10, 2);
        _repositoryMock.Setup(repository => repository.GetBidHistoryAsync(auctionId, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<BidHistoryDto>.Ok(bids, paged));

        var result = await _handler.ExecuteAsync(new GetBidHistoryQuery { TargetAuctionId = auctionId, Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal(auctionId, result.Data[0].AuctionId);
        Assert.Equal(175m, result.Data[1].Amount);
        Assert.Equal("Second Bidder", result.Data[1].UserName);
        _repositoryMock.Verify(repository => repository.GetBidHistoryAsync(auctionId, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Empty_History_When_No_Bids_Exist()
    {
        var auctionId = Guid.NewGuid();
        var paged = PagedResult.Create(1, 10, 0);
        _repositoryMock.Setup(repository => repository.GetBidHistoryAsync(auctionId, 1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<BidHistoryDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetBidHistoryQuery { TargetAuctionId = auctionId, Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal(0, result.PagedResult!.RowCount);
        _repositoryMock.Verify(repository => repository.GetBidHistoryAsync(auctionId, 1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Use_Requested_Pagination_When_Page_And_PageSize_Are_Provided()
    {
        var auctionId = Guid.NewGuid();
        var paged = PagedResult.Create(2, 5, 6);
        _repositoryMock.Setup(repository => repository.GetBidHistoryAsync(auctionId, 2, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<BidHistoryDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetBidHistoryQuery { TargetAuctionId = auctionId, Page = 2, PageSize = 5 });

        Assert.True(result.Success);
        Assert.Equal(2, result.PagedResult!.CurrentPage);
        Assert.Equal(5, result.PagedResult.PageSize);
        _repositoryMock.Verify(repository => repository.GetBidHistoryAsync(auctionId, 2, 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
