using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;

namespace ZetAuction.UnitTests.Commands;

public class CreateUserCommandHandlerTests
{
    private readonly Mock<IUserRepository> _usersMock = new();
    private readonly Mock<IUnitOfWork> _unitOfWorkMock;
    private readonly CreateUserCommandHandler _handler;

    public CreateUserCommandHandlerTests()
    {
        _unitOfWorkMock = TestHelpers.CreateUnitOfWork(_usersMock);
        _handler = new CreateUserCommandHandler(
            _unitOfWorkMock.Object,
            TestHelpers.CreateDateTimeProvider().Object,
            Mock.Of<ILogger<CreateUserCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Create_User_Hash_Password_And_MarkCreated_When_Data_Is_Valid()
    {
        var command = CreateCommand(password: "password123");
        User? addedUser = null;
        _usersMock.Setup(repository => repository.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        _usersMock.Setup(repository => repository.AddAsync(It.IsAny<User>()))
            .Callback<User>(user => addedUser = user)
            .Returns(Task.CompletedTask);

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.NotEqual(Guid.Empty, result.Result.Data);
        Assert.NotNull(addedUser);
        Assert.Equal(command.Name, addedUser.Name);
        Assert.Equal(command.Email, addedUser.Email);
        Assert.Equal(command.Role, addedUser.Role);
        Assert.NotEqual(command.Password, addedUser.PasswordHash);
        Assert.True(BCrypt.Net.BCrypt.Verify(command.Password, addedUser.PasswordHash));
        Assert.Equal(TestHelpers.FixedUtcNow, addedUser.CreatedAtUtc);
        Assert.Equal(TestHelpers.FixedUtcNow, addedUser.UpdatedAtUtc);
        _usersMock.Verify(repository => repository.AddAsync(It.IsAny<User>()), Times.Once);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Email_Already_Exists()
    {
        var command = CreateCommand();
        _usersMock.Setup(repository => repository.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        Assert.Equal("A user with this email already exists.", result.Result.Message);
        _usersMock.Verify(repository => repository.AddAsync(It.IsAny<User>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [InlineData("", "test@example.com", "password123")]
    [InlineData("Test User", "invalid-email", "password123")]
    [InlineData("Test User", "test@example.com", "")]
    public async Task HandleAsync_Should_Fail_When_Data_Is_Invalid(string name, string email, string password)
    {
        var command = CreateCommand(name, email, password, Role.User);
        _usersMock.Setup(repository => repository.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _usersMock.Verify(repository => repository.AddAsync(It.IsAny<User>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Role_Is_Invalid()
    {
        var command = CreateCommand(role: (Role)999);
        _usersMock.Setup(repository => repository.ExistsByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _usersMock.Verify(repository => repository.AddAsync(It.IsAny<User>()), Times.Never);
        _unitOfWorkMock.Verify(work => work.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static CreateUserCommand CreateCommand(
        string name = "Test User",
        string email = "test@example.com",
        string password = "password123",
        Role role = Role.User)
        => new()
        {
            Name = name,
            Email = email,
            Password = password,
            Role = role
        };
}
