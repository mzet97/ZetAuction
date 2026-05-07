using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Queries;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.UnitTests.Queries;

public class GetActiveAuctionsQueryHandlerTests
{
    private readonly Mock<IAuctionReadRepository> _repositoryMock = new();
    private readonly GetActiveAuctionsQueryHandler _handler;

    public GetActiveAuctionsQueryHandlerTests()
    {
        _handler = new GetActiveAuctionsQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetActiveAuctionsQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Empty_List_When_No_Active_Auctions_Exist()
    {
        var paged = PagedResult.Create(1, 10, 0);
        _repositoryMock.Setup(repository => repository.ListActiveAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<AuctionListItemDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetActiveAuctionsQuery { Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal(0, result.PagedResult!.RowCount);
        _repositoryMock.Verify(repository => repository.ListActiveAsync(1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Active_Auctions_When_Auctions_Exist()
    {
        var auctions = new List<AuctionListItemDto>
        {
            new(Guid.NewGuid(), "First", "Description", 100m, 100m, TestHelpers.FixedUtcNow.AddDays(1), AuctionStatus.Active.ToString(), Guid.NewGuid(), null, 0),
            new(Guid.NewGuid(), "Second", "Description", 200m, 250m, TestHelpers.FixedUtcNow.AddDays(2), AuctionStatus.Active.ToString(), Guid.NewGuid(), Guid.NewGuid(), 3)
        };
        var paged = PagedResult.Create(1, 10, 2);
        _repositoryMock.Setup(repository => repository.ListActiveAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<AuctionListItemDto>.Ok(auctions, paged));

        var result = await _handler.ExecuteAsync(new GetActiveAuctionsQuery { Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.All(result.Data, auction => Assert.Equal(AuctionStatus.Active.ToString(), auction.Status));
        Assert.Equal(2, result.PagedResult!.RowCount);
        _repositoryMock.Verify(repository => repository.ListActiveAsync(1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Use_Requested_Pagination_When_Page_And_PageSize_Are_Provided()
    {
        var paged = PagedResult.Create(2, 5, 12);
        _repositoryMock.Setup(repository => repository.ListActiveAsync(2, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<AuctionListItemDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetActiveAuctionsQuery { Page = 2, PageSize = 5 });

        Assert.True(result.Success);
        Assert.Equal(2, result.PagedResult!.CurrentPage);
        Assert.Equal(5, result.PagedResult.PageSize);
        _repositoryMock.Verify(repository => repository.ListActiveAsync(2, 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
