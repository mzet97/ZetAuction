using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Auth.Commands;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.UnitTests.Commands;

public class LoginCommandHandlerTests
{
    private readonly Mock<IUserRepository> _usersMock = new();
    private readonly Mock<IPasswordHasher> _passwordHasherMock = new();
    private readonly Mock<IJwtService> _jwtServiceMock = new();
    private readonly LoginCommandHandler _handler;

    public LoginCommandHandlerTests()
    {
        _handler = new LoginCommandHandler(
            TestHelpers.CreateUnitOfWork(_usersMock).Object,
            _passwordHasherMock.Object,
            _jwtServiceMock.Object,
            Mock.Of<ILogger<LoginCommandHandler>>());
    }

    [Fact]
    public async Task HandleAsync_Should_Return_Token_When_Credentials_Are_Valid()
    {
        var user = TestHelpers.CreateUser(passwordHash: "hashed-password");
        var command = new LoginCommand { Email = user.Email, Password = "password123" };

        _usersMock.Setup(repository => repository.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(hasher => hasher.VerifyPassword(command.Password, user.PasswordHash)).Returns(true);
        _jwtServiceMock.Setup(service => service.GenerateToken(user.Id, user.Email, user.Role.ToString())).Returns("jwt-token");

        var result = await _handler.HandleAsync(command);

        Assert.True(result.Result!.Success);
        Assert.Equal("jwt-token", result.Result.Data!.Token);
        Assert.Equal(user.Id, result.Result.Data.User.Id);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Email_Is_Not_Found()
    {
        var command = new LoginCommand { Email = "missing@example.com", Password = "password123" };

        _usersMock.Setup(repository => repository.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ZetAuction.Domain.Users.User?)null);

        var result = await _handler.HandleAsync(command);

        Assert.False(result.Result!.Success);
        _passwordHasherMock.Verify(hasher => hasher.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        _jwtServiceMock.Verify(service => service.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_Should_Fail_When_Password_Is_Incorrect()
    {
        var user = TestHelpers.CreateUser(passwordHash: "hashed-password");
        var command = new LoginCommand { Email = user.Email, Password = "wrong-password" };

        _usersMock.Setup(repository => repository.GetByEmailAsync(command.Email, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(hasher => hasher.VerifyPassword(command.Password, user.PasswordHash)).Returns(false);

        var result = await _handler.HandleAsync(command);
        Assert.False(result.Result!.Success);
        _jwtServiceMock.Verify(service => service.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never);
    }
}
