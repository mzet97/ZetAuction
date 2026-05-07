using Microsoft.Extensions.Logging;
using Moq;
using ZetAuction.Application.Users.Queries;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Responses;

namespace ZetAuction.UnitTests.Queries;

public class GetAllUsersQueryHandlerTests
{
    private readonly Mock<IUserReadRepository> _repositoryMock = new();
    private readonly GetAllUsersQueryHandler _handler;

    public GetAllUsersQueryHandlerTests()
    {
        _handler = new GetAllUsersQueryHandler(_repositoryMock.Object, Mock.Of<ILogger<GetAllUsersQueryHandler>>());
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Users_When_Users_Exist()
    {
        var users = new List<UserReadDto>
        {
            new(Guid.NewGuid(), "First User", "first@example.com", Role.User.ToString(), TestHelpers.FixedUtcNow),
            new(Guid.NewGuid(), "Admin User", "admin@example.com", Role.Admin.ToString(), TestHelpers.FixedUtcNow)
        };
        var paged = PagedResult.Create(1, 10, 2);
        _repositoryMock.Setup(repository => repository.ListAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<UserReadDto>.Ok(users, paged));

        var result = await _handler.ExecuteAsync(new GetAllUsersQuery { Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Equal(2, result.Data!.Count);
        Assert.Equal("first@example.com", result.Data[0].Email);
        Assert.Equal(Role.Admin.ToString(), result.Data[1].Role);
        _repositoryMock.Verify(repository => repository.ListAsync(1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Return_Empty_List_When_No_Users_Exist()
    {
        var paged = PagedResult.Create(1, 10, 0);
        _repositoryMock.Setup(repository => repository.ListAsync(1, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<UserReadDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetAllUsersQuery { Page = 1, PageSize = 10 });

        Assert.True(result.Success);
        Assert.Empty(result.Data!);
        Assert.Equal(0, result.PagedResult!.RowCount);
        _repositoryMock.Verify(repository => repository.ListAsync(1, 10, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task ExecuteAsync_Should_Use_Requested_Pagination_When_Page_And_PageSize_Are_Provided()
    {
        var paged = PagedResult.Create(2, 5, 7);
        _repositoryMock.Setup(repository => repository.ListAsync(2, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BaseResultList<UserReadDto>.Ok([], paged));

        var result = await _handler.ExecuteAsync(new GetAllUsersQuery { Page = 2, PageSize = 5 });

        Assert.True(result.Success);
        Assert.Equal(2, result.PagedResult!.CurrentPage);
        Assert.Equal(5, result.PagedResult.PageSize);
        _repositoryMock.Verify(repository => repository.ListAsync(2, 5, It.IsAny<CancellationToken>()), Times.Once);
    }
}
