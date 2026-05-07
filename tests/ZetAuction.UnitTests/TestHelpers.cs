using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Services;

namespace ZetAuction.UnitTests;

internal static class TestHelpers
{
    internal static readonly DateTime FixedUtcNow = new(2030, 1, 1, 12, 0, 0, DateTimeKind.Utc);

    internal static Mock<IDateTimeProvider> CreateDateTimeProvider(DateTime? utcNow = null)
    {
        var mock = new Mock<IDateTimeProvider>();
        var now = utcNow ?? FixedUtcNow;
        mock.Setup(provider => provider.UtcNow).Returns(now);
        mock.Setup(provider => provider.Now).Returns(now.ToLocalTime());
        mock.Setup(provider => provider.OffsetUtcNow).Returns(new DateTimeOffset(now));
        mock.Setup(provider => provider.OffsetNow).Returns(new DateTimeOffset(now.ToLocalTime()));
        return mock;
    }

    internal static Mock<IUnitOfWork> CreateUnitOfWork(
        Mock<IUserRepository>? users = null,
        Mock<IAuctionRepository>? auctions = null,
        Mock<IBidRepository>? bids = null)
    {
        var unitOfWork = new Mock<IUnitOfWork>();
        unitOfWork.SetupGet(work => work.Users).Returns((users ?? new Mock<IUserRepository>()).Object);
        unitOfWork.SetupGet(work => work.Auctions).Returns((auctions ?? new Mock<IAuctionRepository>()).Object);
        unitOfWork.SetupGet(work => work.Bids).Returns((bids ?? new Mock<IBidRepository>()).Object);
        unitOfWork.Setup(work => work.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        unitOfWork.Setup(work => work.CommitAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(work => work.RollbackAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(work => work.BeginTransactionAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        unitOfWork.Setup(work => work.ResetTrackingAsync()).Returns(Task.CompletedTask);
        return unitOfWork;
    }

    internal static Auction CreateActiveAuction(decimal currentPrice = 100m, DateTime? endDate = null)
    {
        var auction = new Auction("Test Auction", "Test auction description", 100m, endDate ?? FixedUtcNow.AddDays(1), Guid.NewGuid());
        auction.MarkCreated(FixedUtcNow);
        auction.Activate();

        if (currentPrice > auction.CurrentPrice)
        {
            auction.PlaceBid(Guid.NewGuid(), currentPrice, CreateDateTimeProvider(FixedUtcNow).Object);
            auction.ClearDomainEvents();
        }

        return auction;
    }

    internal static User CreateUser(string email = "test@example.com", string passwordHash = "hash")
    {
        var user = new User("Test User", email, passwordHash, Role.User);
        user.MarkCreated(FixedUtcNow);
        return user;
    }
}
