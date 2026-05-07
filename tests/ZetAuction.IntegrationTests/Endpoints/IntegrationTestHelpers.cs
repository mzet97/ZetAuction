using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ZetAuction.Application.Auth.Commands;
using ZetAuction.Application.Auth.Responses;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Users;
using ZetAuction.Infrastructure.Persistence;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Endpoints;

internal static class IntegrationTestHelpers
{
    internal static async Task AuthenticateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new LoginCommand
        {
            Email = CustomWebApplicationFactory.SeededUserEmail,
            Password = CustomWebApplicationFactory.SeededUserPassword
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BaseResult<AuthResponse>>();
        Assert.False(string.IsNullOrWhiteSpace(result!.Data!.Token));
        AuthenticateAs(client, Guid.Parse("11111111-1111-1111-1111-111111111111"));
    }

    internal static void AuthenticateAs(HttpClient client, Guid userId)
    {
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Test", userId.ToString());
    }

    internal static async Task<Guid> CreateAuctionAsync(HttpClient client, decimal startingPrice = 100m)
    {
        await AuthenticateAsync(client);

        var response = await client.PostAsJsonAsync("/api/v1/auctions", new CreateAuctionCommand
        {
            Name = $"Auction {Guid.NewGuid():N}",
            Description = "Integration auction description",
            StartingBid = startingPrice,
            MinBidIncrement = 1m,
            EndDateTime = DateTime.UtcNow.AddDays(2)
        });
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<BaseResult<Guid>>();
        return result!.Data;
    }

    internal static async Task<Guid> CreateDraftAuctionInDbAsync(CustomWebApplicationFactory factory)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
        var auction = new Auction(
            $"Draft {Guid.NewGuid():N}",
            "Draft auction description",
            startingPrice: 100m,
            minBidIncrement: 1m,
            endDate: DateTime.UtcNow.AddDays(3),
            createdByUserId: factory.SeededUserId);
        auction.MarkCreated(DateTime.UtcNow);
        dbContext.Auctions.Add(auction);
        await dbContext.SaveChangesAsync();
        return auction.Id;
    }

    internal static async Task<Guid> CreateUserInDbAsync(CustomWebApplicationFactory factory, string? email = null)
    {
        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
        var user = new User("Endpoint User", email ?? $"endpoint.{Guid.NewGuid():N}@example.com", BCrypt.Net.BCrypt.HashPassword("Password123!"), Role.User);
        user.MarkCreated(DateTime.UtcNow);
        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        return user.Id;
    }
}
