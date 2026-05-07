using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using ZetAuction.Application.Bids.Commands;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.IntegrationTests.Endpoints;
using ZetAuction.Infrastructure.Persistence;
using ZetAuction.Shared.Responses;

namespace ZetAuction.IntegrationTests.Chaos;

/// <summary>
/// Adversarial scenarios that exercise the moving parts the rest of
/// the suite tends to leave alone: optimistic-concurrency conflicts
/// under contention, rate-limiter accuracy under bursts, and the
/// outbox-driven invariant that <c>BidPlacedEvent</c> count matches
/// the number of accepted bids regardless of how many retries the
/// pipeline burned.
/// </summary>
/// <remarks>
/// These run in the same Testcontainers Postgres image as the rest of
/// the integration suite so they pick up real xmin behaviour. They
/// are intentionally sized to run on a developer laptop and CI agent
/// (~5 seconds each) — the heavier load profile lives in the k6
/// scripts under <c>tests/load/</c>.
/// </remarks>
public class ConcurrencyChaosTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly CustomWebApplicationFactory _factory;

    public ConcurrencyChaosTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Concurrent_Bids_Should_Linearise_Without_Lost_Updates()
    {
        await _factory.ResetDatabaseAsync();
        _factory.ResetRateLimiter();

        // 30 distinct bidders racing on a single auction. The rate
        // limiter is configured to allow many bids per window; what we
        // care about here is that xmin + Polly retry produce a
        // strictly-increasing CurrentPrice with zero lost updates.
        const int bidderCount = 30;
        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(
            CreateAuthenticatedClient(_factory.SeededUserId),
            startingPrice: 100m);

        var bidders = await Task.WhenAll(Enumerable.Range(0, bidderCount)
            .Select(_ => IntegrationTestHelpers.CreateUserInDbAsync(_factory)));

        var startBarrier = new TaskCompletionSource();

        var tasks = bidders.Select((bidderId, index) => Task.Run(async () =>
        {
            using var client = CreateAuthenticatedClient(bidderId);
            await startBarrier.Task;

            var amount = 100m + ((index + 1) * 5m);
            var response = await client.PostAsJsonAsync(
                $"/api/v1/auctions/{auctionId}/bids",
                new PlaceBidCommand { Amount = amount });

            return response.IsSuccessStatusCode;
        })).ToArray();

        startBarrier.SetResult();
        var outcomes = await Task.WhenAll(tasks);

        // With strictly increasing amounts (105, 110, ..., 250) racing
        // concurrently, each accepted bid raises the bar for every
        // pending bidder; lower-amount bidders that retry after a
        // higher one commits cleanly fail with InsufficientBid (not a
        // ConcurrencyConflict that Polly would replay). The realistic
        // floor is therefore much lower than half — what the test
        // actually guards against is the optimistic-concurrency layer
        // dropping ALL updates, which would manifest as 0 or 1 wins.
        var accepted = outcomes.Count(success => success);
        Assert.True(accepted >= 3,
            $"Expected at least 3 bids to win the race, got {accepted}. " +
            "The retry pipeline is dropping updates instead of serialising them.");

        // Inspect the durable state: the auction's CurrentPrice must
        // equal the maximum amount across the accepted bids, and the
        // bid count in the database must equal the number of HTTP 2xx
        // responses observed by the clients.
        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();

        var bidsInDb = dbContext.Bids
            .Where(b => b.AuctionId == auctionId)
            .OrderBy(b => b.Amount)
            .ToList();

        Assert.Equal(accepted, bidsInDb.Count);

        var auction = dbContext.Auctions.Single(a => a.Id == auctionId);
        Assert.Equal(bidsInDb.Max(b => b.Amount), auction.CurrentPrice);
    }

    [Fact]
    public async Task Bid_Placement_Endpoint_Should_Stay_Within_Status_Contract_Under_Burst()
    {
        await _factory.ResetDatabaseAsync();
        _factory.ResetRateLimiter();

        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(
            CreateAuthenticatedClient(_factory.SeededUserId),
            startingPrice: 100m);

        // 100 sequential bids by the seeded bidder (above the 5/5min
        // limit) — the rate limiter must reject the surplus, the
        // domain must reject the rest cleanly, and at no point should
        // we see a 5xx.
        var bidderId = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        using var client = CreateAuthenticatedClient(bidderId);

        var statuses = new List<int>();
        for (var i = 0; i < 100; i++)
        {
            var response = await client.PostAsJsonAsync(
                $"/api/v1/auctions/{auctionId}/bids",
                new PlaceBidCommand { Amount = 110m + i });

            statuses.Add((int)response.StatusCode);
        }

        Assert.DoesNotContain(statuses, s => s >= 500);

        // We should have at least one 2xx (the first valid bid) and
        // some 4xx (rate limit + insufficient amount + concurrency).
        Assert.Contains(statuses, s => s >= 200 && s < 300);
        Assert.Contains(statuses, s => s >= 400 && s < 500);
    }

    [Fact]
    public async Task Highest_Bid_Cache_Stays_Consistent_Under_Concurrent_Reads_And_Writes()
    {
        await _factory.ResetDatabaseAsync();
        _factory.ResetRateLimiter();

        var auctionId = await IntegrationTestHelpers.CreateAuctionAsync(
            CreateAuthenticatedClient(_factory.SeededUserId),
            startingPrice: 100m);

        var bidder = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        using var bidderClient = CreateAuthenticatedClient(bidder);

        var observer = await IntegrationTestHelpers.CreateUserInDbAsync(_factory);
        using var observerClient = CreateAuthenticatedClient(observer);

        // Place increasing bids while a background task hammers the
        // highest-bid endpoint. The observer must never see the cache
        // regress — that was the bug Phase 4 closed.
        using var cts = new CancellationTokenSource();
        var observed = new List<decimal>();
        var observerLock = new object();

        var observerTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                var response = await observerClient.GetAsync(
                    $"/api/v1/auctions/{auctionId}/bids/highest",
                    cts.Token);

                if (response.IsSuccessStatusCode)
                {
                    var payload = await response.Content.ReadFromJsonAsync<BaseResult<BidViewModel>>(cts.Token);
                    if (payload?.Data is { } bid)
                    {
                        lock (observerLock)
                        {
                            observed.Add(bid.Amount);
                        }
                    }
                }
            }
        }, cts.Token);

        for (var i = 1; i <= 25; i++)
        {
            await bidderClient.PostAsJsonAsync(
                $"/api/v1/auctions/{auctionId}/bids",
                new PlaceBidCommand { Amount = 100m + (i * 10m) });
        }

        cts.Cancel();
        try { await observerTask; }
        catch (OperationCanceledException) { }

        lock (observerLock)
        {
            for (var i = 1; i < observed.Count; i++)
            {
                Assert.True(
                    observed[i] >= observed[i - 1],
                    $"Highest-bid regressed from {observed[i - 1]} to {observed[i]} at index {i}.");
            }
        }
    }

    private HttpClient CreateAuthenticatedClient(Guid userId)
    {
        var client = _factory.CreateClient();
        IntegrationTestHelpers.AuthenticateAs(client, userId);
        return client;
    }
}
