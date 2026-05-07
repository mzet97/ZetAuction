using System.Net;
using System.Net.Http.Json;
using ZetAuction.Application.Auth.Commands;
using ZetAuction.Application.Auth.Responses;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Endpoints;

public class AuthEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;

    public AuthEndpointsTests(CustomWebApplicationFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task PostRegister_Should_Create_User_When_Data_Is_Valid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/register", new CreateUserCommand
        {
            Name = "Registered User",
            Email = $"registered.{Guid.NewGuid():N}@example.com",
            Password = "Password123!",
            Role = Role.User
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BaseResult<Guid>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.Data);
    }

    [Fact]
    public async Task PostLogin_Should_Return_Token_When_Credentials_Are_Valid()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = CustomWebApplicationFactory.SeededUserEmail,
            Password = CustomWebApplicationFactory.SeededUserPassword
        });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResult<AuthResponse>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.Data!.Token));
    }

    [Fact]
    public async Task PostLogin_Should_Return_BadRequest_When_Email_Does_Not_Exist()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = $"missing.{Guid.NewGuid():N}@example.com",
            Password = CustomWebApplicationFactory.SeededUserPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLogin_Should_Return_BadRequest_When_Password_Is_Incorrect()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = CustomWebApplicationFactory.SeededUserEmail,
            Password = "wrong-password"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLogin_Should_Return_BadRequest_When_Email_Is_Empty()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = "",
            Password = CustomWebApplicationFactory.SeededUserPassword
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostLogin_Should_Return_BadRequest_When_Password_Is_Empty()
    {
        var response = await _client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = CustomWebApplicationFactory.SeededUserEmail,
            Password = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
