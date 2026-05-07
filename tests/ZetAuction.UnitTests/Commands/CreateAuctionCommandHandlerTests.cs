using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class CreateAuctionCommandHandlerTests
{
    private readonly Mock<IAuctionRepository> _auctionsMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CreateAuctionCommandHandler _handler;

    public CreateAuctionCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(auctions: _auctionsMock);
        _handler = new CreateAuctionCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<CreateAuctionCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Create_Activate_MarkCreated_And_Save_When_Data_Is_Valid()
    {
        var command = CreateCommand();
        Auction? addedAuction = null;
        _auctionsMock.Setup(repository => repository.AddAsync(It.IsAny<Auction>()))
            .Callback<Auction>(auction => addedAuction = auction)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.NotEqual(Guid.Empty, result.Result.Data);
        Assert.NotNull(addedAuction);
        Assert.Equal(AuctionStatus.Active, addedAuction.Status);
        Assert.True(addedAuction.IsValid());
        Assert.Equal(TestHelpers.FixedUtcNow, addedAuction.CreatedAtUtc);
        Assert.Equal(TestHelpers.FixedUtcNow, addedAuction.UpdatedAtUtc);
        _auctionsMock.Verify(repository => repository.AddAsync(It.IsAny<Auction>()), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData("", "Description", 100)]
    [InlineData("Auction", "Description", 0)]
    [InlineData("Auction", "Description", -1)]
    public async Task HandleAsync_Should_Fail_When_Data_Is_Invalid(string title, string description, decimal startingPrice)
    {
        var command = CreateCommand(title, description, startingPrice, TestHelpers.FixedUtcNow.AddDays(1));

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.AddAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_EndDate_Is_In_The_Past()
    {
        var command = CreateCommand(endDate: DateTime.UtcNow.AddDays(-1));

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _auctionsMock.Verify(repository => repository.AddAsync(It.IsAny<Auction>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateAuctionCommand CreateCommand(
        string title = "Auction",
        string description = "Description",
        decimal startingPrice = 100m,
        DateTime? endDate = null)
        => new()
        {
            Name = title,
            Description = description,
            StartingBid = startingPrice,
            MinBidIncrement = 1m,
            EndDateTime = endDate ?? TestHelpers.FixedUtcNow.AddDays(1),
            CreatedByUserId = Guid.NewGuid()
        };
}
