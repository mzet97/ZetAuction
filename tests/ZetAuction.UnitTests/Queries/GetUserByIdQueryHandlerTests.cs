using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Users.Queries;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;

namespace ZetAuction.UnitTests.Queries;

public class GetUserByIdQueryHandlerTests
{
    private readonly Mock<IUserReadRepository> _repositoryMock = new();
    private readonly GetUserByIdQueryHandler _handler;

    public GetUserByIdQueryHandlerTests()
    {
        _handler = new GetUserByIdQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetUserByIdQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_User_When_User_Exists()
    {
        var userId = Guid.NewGuid();
        var user = new UserReadDto(userId, "Test User", "test@example.com", Role.Admin.ToString(), TestHelpers.FixedUtcNow);
        _repositoryMock.Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);

        var result = await _handler.ExecuteAsync(new GetUserByIdQuery { UserId = userId });

        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Equal(userId, result.Data.Id);
        Assert.Equal("Test User", result.Data.Name);
        Assert.Equal("test@example.com", result.Data.Email);
        Assert.Equal(Role.Admin.ToString(), result.Data.Role);
        Assert.Equal(TestHelpers.FixedUtcNow, result.Data.CreatedAtUtc);
        _repositoryMock.Verify(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Fail_When_User_Is_Not_Found()
    {
        var userId = Guid.NewGuid();
        _repositoryMock.Setup(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((UserReadDto?)null);

        var result = await _handler.ExecuteAsync(new GetUserByIdQuery { UserId = userId });

        Assert.False(result.Success);
        Assert.Null(result.Data);
        Assert.Equal("User not found.", result.Message);
        _repositoryMock.Verify(repository => repository.GetByIdAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
    }
}
