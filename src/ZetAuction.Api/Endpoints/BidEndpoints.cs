using Paramore.Brighter;
using Paramore.Darker;
using System.Security.Claims;
using ZetAuction.Api.Common;
using ZetAuction.Application.Bids.Commands;
using ZetAuction.Application.Bids.Queries;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Api.Endpoints;

public sealed class BidEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        // Phase 5 collapses the public bid surface to the route shape
        // mandated by the assessment specification. The Phase 0/1 era
        // legacy duplicates (POST /api/bids and GET
        // /api/auctions/{id}/highest-bid) are gone.
        endpoints.MapPost("/api/v1/auctions/{auctionId:guid}/bids", PlaceBidForAuctionAsync)
            .WithName("PlaceBid")
            .WithTags("Bids")
            .WithSummary("Place a bid on an active auction")
            .WithDescription("Authenticated callers other than the auction creator can place a bid. Amount must be ≥ startingBid (first bid) or ≥ currentPrice + minBidIncrement (subsequent bids).")
            .Produces<BaseResult>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .Produces(StatusCodes.Status429TooManyRequests)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/auctions/{auctionId:guid}/bids", GetBidHistoryAsync)
            .WithName("GetBidHistory")
            .WithTags("Bids")
            .WithSummary("Get paginated bid history for an auction")
            .Produces<BaseResultList<BidViewModel>>()
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/auctions/{auctionId:guid}/bids/highest", GetHighestBidAsync)
            .WithName("GetAuctionHighestBid")
            .WithTags("Bids")
            .WithSummary("Get the highest bid for an auction")
            .WithDescription("High-traffic endpoint. Backed by a versioned Redis cache; the response is also marked Cache-Control: private, max-age=1 so well-behaved clients coalesce poll bursts.")
            .Produces<BaseResult<BidViewModel>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();
    }

    private static Task<IResult> PlaceBidForAuctionAsync(
        IAmACommandProcessor commandProcessor,
        Guid auctionId,
        PlaceBidCommand command,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        command.AuctionId = auctionId;
        return PlaceBidAsync(commandProcessor, command, user, cancellationToken);
    }

    private static async Task<IResult> PlaceBidAsync(
        IAmACommandProcessor commandProcessor,
        PlaceBidCommand command,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var guid))
        {
            return Results.Unauthorized();
        }

        command.UserId = guid;

        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        if (result.Success)
        {
            return Results.Ok(result);
        }

        if (result.Message?.Contains("rate limit", StringComparison.OrdinalIgnoreCase) == true)
        {
            return Results.Json(result, statusCode: StatusCodes.Status429TooManyRequests);
        }

        return Results.BadRequest(result);
    }

    private static async Task<IResult> GetBidHistoryAsync(
        IQueryProcessor queryProcessor,
        Guid auctionId,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetBidHistoryQuery
        {
            TargetAuctionId = auctionId,
            Page = page,
            PageSize = pageSize
        };

        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetHighestBidAsync(
        IQueryProcessor queryProcessor,
        HttpContext httpContext,
        Guid auctionId,
        CancellationToken cancellationToken)
    {
        var query = new GetHighestBidQuery { TargetAuctionId = auctionId };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        if (!result.Success)
        {
            return Results.NotFound(result);
        }

        // Bid amount is monotonic per auction; we use it as a weak ETag
        // to let clients short-circuit polls of unchanged state. The
        // server still does the work, but the client can drop the body
        // when it sees a 304-equivalent — a future iteration can wire
        // up If-None-Match conditional logic.
        httpContext.Response.Headers.CacheControl = "private, max-age=1";
        if (result.Data is not null)
        {
            httpContext.Response.Headers.ETag = $"\"{result.Data.Amount}-{result.Data.PlacedAtUtc.Ticks}\"";
        }

        return Results.Ok(result);
    }
}
