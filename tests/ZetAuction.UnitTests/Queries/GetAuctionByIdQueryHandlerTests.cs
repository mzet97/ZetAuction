using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Queries;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Queries;

public class GetAuctionByIdQueryHandlerTests
{
    private readonly Mock<IAuctionReadRepository> _repositoryMock = new();
    private readonly GetAuctionByIdQueryHandler _handler;

    public GetAuctionByIdQueryHandlerTests()
    {
        _handler = new GetAuctionByIdQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetAuctionByIdQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Auction_Dto_When_Auction_Exists()
    {
        var auctionId = Guid.NewGuid();
        var createdByUserId = Guid.NewGuid();
        var winnerId = Guid.NewGuid();
        var dto = new AuctionDetailsDto(auctionId, "Auction", "Description", 100m, 125m, TestHelpers.FixedUtcNow.AddDays(1), AuctionStatus.Active.ToString(), createdByUserId, winnerId, 2);
        _repositoryMock.Setup(repository => repository.GetDetailsByIdAsync(auctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(dto);

        var result = await _handler.ExecuteAsync(new GetAuctionByIdQuery { AuctionId = auctionId });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(auctionId, result.Data.Id);
        Assert.Equal("Auction", result.Data.Name);
        Assert.Equal("Description", result.Data.Description);
        Assert.Equal(100m, result.Data.StartingBid);
        Assert.Equal(125m, result.Data.CurrentPrice);
        Assert.Equal(AuctionStatus.Active.ToString(), result.Data.Status);
        Assert.Equal(createdByUserId, result.Data.CreatedByUserId);
        Assert.Equal(winnerId, result.Data.WinnerId);
        Assert.Equal(2, result.Data.BidCount);
        _repositoryMock.Verify(repository => repository.GetDetailsByIdAsync(auctionId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_Auction_Is_Not_Found()
    {
        var auctionId = Guid.NewGuid();
        _repositoryMock.Setup(repository => repository.GetDetailsByIdAsync(auctionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuctionDetailsDto?)null);

        var result = await _handler.ExecuteAsync(new GetAuctionByIdQuery { AuctionId = auctionId });

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("Auction not found.", result.Message);
        _repositoryMock.Verify(repository => repository.GetDetailsByIdAsync(auctionId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
