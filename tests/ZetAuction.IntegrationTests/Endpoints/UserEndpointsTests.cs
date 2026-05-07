using System.Net;
using System.Net.Http.Json;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Endpoints;

public class UserEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public UserEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostUsers_Should_Create_User_When_Data_Is_Valid()
    {
        var command = CreateUserCommand();

        var response = await _client.PostAsJsonAsync("/api/v1/users", command);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BaseResult<Guid>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.Data);
    }

    [Fact]
    public async Task PostUsers_Should_Return_BadRequest_When_Email_Is_Duplicated()
    {
        var command = CreateUserCommand(email: CustomWebApplicationFactory.SeededUserEmail);

        var response = await _client.PostAsJsonAsync("/api/v1/users", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostUsers_Should_Return_BadRequest_When_Data_Is_Invalid()
    {
        var command = CreateUserCommand(name: "", email: "invalid-email", password: "");

        var response = await _client.PostAsJsonAsync("/api/v1/users", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_Should_Return_Unauthorized_When_Request_Is_Unauthenticated()
    {
        var response = await _client.GetAsync("/api/v1/users");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetUsers_Should_Return_Ok_When_Request_Is_Authenticated()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync("/api/v1/users");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResultList<UserViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data, user => user.Email == CustomWebApplicationFactory.SeededUserEmail);
    }

    [Fact]
    public async Task GetUserById_Should_Return_User_When_User_Exists()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/users/{_factory.SeededUserId}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResult<UserViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(_factory.SeededUserId, result.Data!.Id);
    }

    [Fact]
    public async Task GetUserById_Should_Return_NotFound_When_User_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PutUser_Should_Return_Ok_When_User_Exists()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var userId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        var command = new UpdateUserCommand { Name = "Updated Endpoint User", Email = $"updated.{Guid.NewGuid():N}@example.com" };

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{userId}", command);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PutUser_Should_Return_BadRequest_When_User_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var command = new UpdateUserCommand { Name = "Updated Endpoint User", Email = $"updated.{Guid.NewGuid():N}@example.com" };

        var response = await _client.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task DeleteUser_Should_Return_Ok_When_User_Exists()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var userId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);

        var response = await _client.DeleteAsync($"/api/v1/users/{userId}");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task DeleteUser_Should_Return_NotFound_When_User_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.DeleteAsync($"/api/v1/users/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static CreateUserCommand CreateUserCommand(
        string name = "New Integration User",
        string? email = null,
        string password = "Password123!")
        => new()
        {
            Name = name,
            Email = email ?? $"user.{Guid.NewGuid():N}@example.com",
            Password = password,
            Role = Role.User
        };
}
