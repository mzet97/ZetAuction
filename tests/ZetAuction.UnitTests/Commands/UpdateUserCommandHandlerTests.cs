using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;

namespace ZetAuction.UnitTests.Commands;

public class UpdateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _usersMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly UpdateUserCommandHandler _handler;

    public UpdateUserCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(_usersMock);
        _handler = new UpdateUserCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<UpdateUserCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Update_Profile_MarkUpdated_And_Save_When_User_Exists()
    {
        var user = TestHelpers.CreateUser();
        var command = new UpdateUserCommand { UserId = user.Id, Name = "Updated User", Email = "updated@example.com" };
        _usersMock.Setup(repository => repository.GetByIdAsync(command.UserId)).ReturnsAsync(user);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal(command.Name, user.Name);
        Assert.Equal(command.Email, user.Email);
        Assert.Equal(TestHelpers.FixedUtcNow, user.UpdatedAtUtc);
        _usersMock.Verify(repository => repository.UpdateAsync(It.Is<User>(updated => updated.Id == user.Id && updated.Name == command.Name && updated.Email == command.Email)), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_User_Is_Not_Found()
    {
        var command = new UpdateUserCommand { UserId = Guid.NewGuid(), Name = "Updated User", Email = "updated@example.com" };
        _usersMock.Setup(repository => repository.GetByIdAsync(command.UserId)).ReturnsAsync((User?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("User not found.", result.Result.Message);
        _usersMock.Verify(repository => repository.UpdateAsync(It.IsAny<User>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Data_Is_Invalid_After_Update()
    {
        var user = TestHelpers.CreateUser();
        var command = new UpdateUserCommand { UserId = user.Id, Name = "Updated User", Email = "invalid-email" };
        _usersMock.Setup(repository => repository.GetByIdAsync(command.UserId)).ReturnsAsync(user);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("Updated User", user.Name);
        Assert.Equal("invalid-email", user.Email);
        _usersMock.Verify(repository => repository.UpdateAsync(It.IsAny<User>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}
