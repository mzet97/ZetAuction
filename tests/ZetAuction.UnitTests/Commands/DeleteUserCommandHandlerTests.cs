using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;

namespace ZetAuction.UnitTests.Commands;

public class DeleteUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _usersMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly DeleteUserCommandHandler _handler;

    public DeleteUserCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(_usersMock);
        _handler = new DeleteUserCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<DeleteUserCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_SoftDelete_And_Update_User_When_User_Exists()
    {
        var user = TestHelpers.CreateUser();
        var command = new DeleteUserCommand { UserId = user.Id };
        _usersMock.Setup(repository => repository.GetByIdAsync(command.UserId)).ReturnsAsync(user);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.True(user.IsDeleted);
        Assert.Equal(TestHelpers.FixedUtcNow, user.DeletedAtUtc);
        _usersMock.Verify(repository => repository.UpdateAsync(user), Times.Once);
        _usersMock.Verify(repository => repository.RemoveAsync(It.IsAny<Guid>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_User_Is_Not_Found()
    {
        var command = new DeleteUserCommand { UserId = Guid.NewGuid() };
        _usersMock.Setup(repository => repository.GetByIdAsync(command.UserId)).ReturnsAsync((User?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("User not found.", result.Result.Message);
        _usersMock.Verify(repository => repository.UpdateAsync(It.IsAny<User>()), Times.Never);
        _usersMock.Verify(repository => repository.RemoveAsync(It.IsAny<Guid>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
