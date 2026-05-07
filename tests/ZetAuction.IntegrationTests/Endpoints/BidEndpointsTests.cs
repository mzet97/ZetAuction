using System.Net;
using System.Net.Http.Json;
using ZetAuction.Application.Bids.Commands;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Endpoints;

public class BidEndpointsTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public BidEndpointsTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetBidHistory_Should_Return_List_When_Auction_Exists()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/auctions/{_factory.SeededAuctionId}/bids");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResultList<BidViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.NotNull(result.Data);
    }

    [Fact]
    public async Task GetBidHistory_Should_Return_Empty_List_When_Auction_Does_Not_Exist()
    {
        await IntegrationTestHelpers.AuthenticateAsync(_client);

        var response = await _client.GetAsync($"/api/v1/auctions/{Guid.NewGuid()}/bids");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResultList<BidViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Empty(result.Data!);
    }

    [Fact]
    public async Task GetHighestBid_Should_Return_Bid_When_Bids_Exist()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);
        var bidResponse = await _client.PostAsJsonAsync(
            $"/api/v1/auctions/{auctionId}/bids",
            new PlaceBidCommand { Amount = 150m });
        bidResponse.EnsureSuccessStatusCode();

        var response = await _client.GetAsync($"/api/v1/auctions/{auctionId}/bids/highest");

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResult<BidViewModel>>();
        Assert.NotNull(result);
        Assert.True(result.Success);
        Assert.Equal(150m, result.Data!.Amount);
        Assert.Equal("Endpoint User", result.Data.UserName);
        // Phase 5: highest-bid carries Cache-Control + ETag so polling
        // clients can de-duplicate identical responses.
        Assert.NotNull(response.Headers.CacheControl);
        Assert.NotNull(response.Headers.ETag);
    }

    [Fact]
    public async Task GetHighestBid_Should_Return_NotFound_When_No_Bids_Exist()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);

        var response = await _client.GetAsync($"/api/v1/auctions/{auctionId}/bids/highest");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PostBids_Should_Place_Bid_When_Request_Is_Authenticated_And_Amount_Is_Valid()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);
        var command = new PlaceBidCommand { Amount = 150m };

        var response = await _client.PostAsJsonAsync($"/api/v1/auctions/{auctionId}/bids", command);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<BaseResult>();
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    [Fact]
    public async Task PostBids_Should_Return_TooManyRequests_On_Sixth_Bid_In_Rate_Limit_Window()
    {
        _factory.ResetRateLimiter();
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            var allowedResponse = await _client.PostAsJsonAsync(
                $"/api/v1/auctions/{auctionId}/bids",
                new PlaceBidCommand { Amount = 100m + attempt });
            allowedResponse.EnsureSuccessStatusCode();
        }

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/auctions/{auctionId}/bids",
            new PlaceBidCommand { Amount = 106m });

        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task PostBids_Should_Keep_Consistent_Highest_Bid_When_Submitted_Concurrently()
    {
        _factory.ResetRateLimiter();
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidRequests = new List<(HttpClient Client, decimal Amount)>();

        for (var index = 0; index < 5; index++)
        {
            var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
            var client = _factory.CreateClient();
            IntegrationTestHelpers.AuthenticateAs(client, bidderId);
            bidRequests.Add((client, 110m + index * 10m));
        }

        var responses = await Task.WhenAll(bidRequests.Select(request =>
            request.Client.PostAsJsonAsync(
                $"/api/v1/auctions/{auctionId}/bids",
                new PlaceBidCommand { Amount = request.Amount })));
        var successfulAmounts = responses
            .Select((response, index) => new { response, bidRequests[index].Amount })
            .Where(item => item.response.IsSuccessStatusCode)
            .Select(item => item.Amount)
            .ToList();

        Assert.NotEmpty(successfulAmounts);
        var highestResponse = await _client.GetAsync($"/api/v1/auctions/{auctionId}/bids/highest");

        highestResponse.EnsureSuccessStatusCode();
        var highestResult = await highestResponse.Content.ReadFromJsonAsync<BaseResult<BidViewModel>>();
        Assert.NotNull(highestResult);
        Assert.True(highestResult.Success);
        Assert.Equal(successfulAmounts.Max(), highestResult.Data!.Amount);
    }

    [Fact]
    public async Task PostBids_Should_Return_UnprocessableEntity_When_Creator_Bids_On_Own_Auction()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var command = new PlaceBidCommand { Amount = 150m };

        var response = await _client.PostAsJsonAsync($"/api/v1/auctions/{auctionId}/bids", command);

        // InvalidBidException maps to RFC 7807 ProblemDetails with status
        // 422 (Unprocessable Entity) and type
        // https://zetauction.dev/problems/invalid-bid.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task PostBids_Should_Return_Unauthorized_When_Request_Is_Unauthenticated()
    {
        var command = new PlaceBidCommand { Amount = 150m };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/auctions/{_factory.SeededAuctionId}/bids",
            command);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostBids_Should_Return_BadRequest_When_Auction_Does_Not_Exist()
    {
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);
        var command = new PlaceBidCommand { Amount = 150m };

        var response = await _client.PostAsJsonAsync(
            $"/api/v1/auctions/{Guid.NewGuid()}/bids",
            command);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task PostBids_Should_Return_UnprocessableEntity_When_Amount_Is_Insufficient()
    {
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(_client, startingPrice: 100m);
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        IntegrationTestHelpers.AuthenticateAs(_client, bidderId);
        var firstBidResponse = await _client.PostAsJsonAsync(
            $"/api/v1/auctions/{auctionId}/bids",
            new PlaceBidCommand { Amount = 100m });
        firstBidResponse.EnsureSuccessStatusCode();
        var command = new PlaceBidCommand { Amount = 100m };

        var response = await _client.PostAsJsonAsync($"/api/v1/auctions/{auctionId}/bids", command);

        // InsufficientBidAmountException maps to RFC 7807 ProblemDetails
        // with status 422 and exposes bidAmount/requiredAmount as
        // typed extensions so clients can render a precise message.
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
