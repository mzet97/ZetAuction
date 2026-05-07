using System.Net;
using System.Net.Http.Json;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Domain.Auctions;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Endpoints;

public class AuctionEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuctionEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetAuctions_Should_Return_List_When_Request_Is_Authenticated()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var draftAuctionId = await IntegrationTestHelpers.CreateDraftAuctionInDbAsync(_factory);

        var response = await _client.GetAsync("/api/v1/auctions");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResultList<AuctionViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
        Assert.Contains(result.Data, auction => auction.Id == _factory.SeededAuctionId);
        Assert.Contains(result.Data, auction => auction.Id == draftAuctionId && auction.Status == AuctionStatus.Draft.ToString());
    }

    [Fact]
    public async Task GetAuctions_Should_Filter_By_Status_With_Pagination_When_Status_Is_Provided()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var finalizedAuctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var closeResponse = await _client.PostAsync($"/api/v1/auctions/{finalizedAuctionId}/close", null);
        closeResponse.EnsureSuccessStatusCode();

        var activeResponse = await _client.GetAsync("/api/v1/auctions?status=active&page=1&pageSize=5");
        var finalizedResponse = await _client.GetAsync("/api/v1/auctions?status=finalized&page=1&pageSize=5");

        activeResponse.EnsureSuccessStatusCode();
        finalizedResponse.EnsureSuccessStatusCode();
        var activeResult = await activeResponse.Content.ReadFromJsonAsync<BaseResultList<AuctionViewModel>>();
        var finalizedResult = await finalizedResponse.Content.ReadFromJsonAsync<BaseResultList<AuctionViewModel>>();
        Assert.NotNull(activeResult);
        Assert.NotNull(finalizedResult);
        Assert.True(activeResult.Success);
        Assert.True(finalizedResult.Success);
        Assert.All(activeResult.Data!, auction => Assert.Equal(AuctionStatus.Active.ToString(), auction.Status));
        Assert.All(finalizedResult.Data!, auction => Assert.Equal(AuctionStatus.Finalized.ToString(), auction.Status));
        Assert.Contains(finalizedResult.Data!, auction => auction.Id == finalizedAuctionId);
    }

    [Fact]
    public async Task GetActiveAuctions_Should_Return_Only_Active_Auctions_When_Auctions_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync("/api/v1/auctions/active");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResultList<AuctionViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.All(result.Data!, auction => Assert.Equal(AuctionStatus.Active.ToString(), auction.Status));
    }

    [Fact]
    public async Task GetAuctionById_Should_Return_Auction_When_Auction_Exists()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/auctions/{_factory.SeededAuctionId}");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResult<AuctionViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(_factory.SeededAuctionId, result.Data!.Id);
    }

    [Fact]
    public async Task GetAuctionById_Should_Return_NotFound_When_Auction_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/auctions/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostAuctions_Should_Return_Unauthorized_When_Request_Is_Unauthenticated()
    {
        var command = CreateAuctionCommand();

        var response = await _client.PostAsJsonAsync("/api/v1/auctions", command);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAuctions_Should_Return_BadRequest_When_Request_Is_Authenticated_And_Data_Is_Invalid()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var command = CreateAuctionCommand(title: "", startingPrice: 0m);

        var response = await _client.PostAsJsonAsync("/api/v1/auctions", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostAuctions_Should_Create_Auction_When_Request_Is_Authenticated_And_Data_Is_Valid()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var command = CreateAuctionCommand(title: $"Created {Guid.NewGuid():N}", startingPrice: 250m);

        var response = await _client.PostAsJsonAsync("/api/v1/auctions", command);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<BaseResult<Guid>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotEqual(Guid.Empty, result.Data);
    }

    [Fact]
    public async Task PutAuction_Should_Return_Ok_When_Auction_Exists_And_Is_Draft()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var auctionId = await IntegrationTestHelpers.CreateDraftAuctionInDbAsync(_factory);
        var command = new UpdateAuctionCommand { Title = "Updated", Description = "Updated description", EndDate = DateTime.UtcNow.AddDays(5) };

        var response = await _client.PutAsJsonAsync($"/api/v1/auctions/{auctionId}", command);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PutAuction_Should_Return_BadRequest_When_Auction_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);
        var command = new UpdateAuctionCommand { Title = "Updated", Description = "Updated description", EndDate = DateTime.UtcNow.AddDays(5) };

        var response = await _client.PutAsJsonAsync($"/api/v1/auctions/{Guid.NewGuid()}", command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostCloseAuction_Should_Return_Ok_When_Auction_Is_Active()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client);

        var response = await _client.PostAsync($"/api/v1/auctions/{auctionId}/close", null);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task PostCloseAuction_Should_Finalize_Winning_Fields_When_Auction_Has_Bids()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);
        var bidResponse = await _client.PostAsJsonAsync($"/api/v1/auctions/{auctionId}/bids", new ZetAuction.Application.Bids.Commands.PlaceBidCommand { Amount = 150m });
        bidResponse.EnsureSuccessStatusCode();
        IntegrationTestHelpers.AuthenticateAs(_client, _factory.SeededUserId);

        var closeResponse = await _client.PostAsync($"/api/v1/auctions/{auctionId}/close", null);
        var getResponse = await _client.GetAsync($"/api/v1/auctions/{auctionId}");

        closeResponse.EnsureSuccessStatusCode();
        getResponse.EnsureSuccessStatusCode();
        var result = await getResponse.Content.ReadFromJsonAsync<BaseResult<AuctionViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(AuctionStatus.Finalized.ToString(), result.Data!.Status);
        Assert.Equal(bidderId, result.Data.WinnerId);
        Assert.NotNull(result.Data.WinningBidId);
        Assert.Equal(150m, result.Data.WinningAmount);
        Assert.NotNull(result.Data.FinalizedAtUtc);
    }

    [Fact]
    public async Task PostCloseAuction_Should_Return_BadRequest_When_Auction_Is_Already_Closed()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client);
        var firstResponse = await _client.PostAsync($"/api/v1/auctions/{auctionId}/close", null);
        firstResponse.EnsureSuccessStatusCode();

        var secondResponse = await _client.PostAsync($"/api/v1/auctions/{auctionId}/close", null);

        Assert.Equal(HttpStatusCode.BadRequest, secondResponse.StatusCode);
    }

    [Fact]
    public async Task PostCancelAuction_Should_Return_Ok_When_Auction_Can_Be_Cancelled()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client);

        var response = await _client.PostAsync($"/api/v1/auctions/{auctionId}/cancel?reason=test", null);

        response.EnsureSuccessStatusCode();
    }

    private static CreateAuctionCommand CreateAuctionCommand(string title = "Auction", decimal startingPrice = 100m)
        => new()
        {
            Name = title,
            Description = "Created from integration test",
            StartingBid = startingPrice,
            MinBidIncrement = 1m,
            EndDateTime = DateTime.UtcNow.AddDays(3)
        };
}
