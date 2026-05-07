using Paramore.Brighter;
using Paramore.Darker;
using System.Security.Claims;
using ZetAuction.Api.Common;
using ZetAuction.Application.Auctions.Commands;
using ZetAuction.Application.Auctions.Queries;
using ZetAuction.Application.Auctions.ViewModels;
using ZetAuction.Application.Common.Messaging;
using ZetAuction.Domain.Auctions;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Api.Endpoints;

public sealed class AuctionEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auctions", CreateAuctionAsync)
            .WithName("CreateAuction")
            .WithTags("Auctions")
            .WithSummary("Create a new auction")
            .WithDescription("Creates an auction owned by the authenticated user. The endDateTime must be in the future and startingBid > 0.")
            .Produces<BaseResult<Guid>>(StatusCodes.Status201Created)
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/auctions", GetAuctionsAsync)
            .WithName("GetAuctions")
            .WithTags("Auctions")
            .WithSummary("List auctions with optional status filter and pagination")
            .Produces<BaseResultList<AuctionViewModel>>()
            .Produces(StatusCodes.Status422UnprocessableEntity)
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/auctions/active", GetActiveAuctionsAsync)
            .WithName("GetActiveAuctions")
            .WithTags("Auctions")
            .WithSummary("Get currently active auctions (paginated)")
            .Produces<BaseResultList<AuctionViewModel>>()
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/auctions/{id:guid}", GetAuctionByIdAsync)
            .WithName("GetAuctionById")
            .WithTags("Auctions")
            .WithSummary("Get a single auction by identifier")
            .Produces<BaseResult<AuctionViewModel>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        endpoints.MapPut("/api/v1/auctions/{id:guid}", UpdateAuctionAsync)
            .WithName("UpdateAuction")
            .WithTags("Auctions")
            .WithSummary("Update an auction's details (draft only)")
            .Produces<BaseResult>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/auctions/{id:guid}/close", CloseAuctionAsync)
            .WithName("CloseAuction")
            .WithTags("Auctions")
            .WithSummary("Close an active auction and determine the winner")
            .Produces<BaseResult>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        endpoints.MapPost("/api/v1/auctions/{id:guid}/cancel", CancelAuctionAsync)
            .WithName("CancelAuction")
            .WithTags("Auctions")
            .WithSummary("Cancel an auction with an optional reason")
            .Produces<BaseResult>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateAuctionAsync(
        IAmACommandProcessor commandProcessor,
        CreateAuctionCommand command,
        ClaimsPrincipal user,
        CancellationToken cancellationToken)
    {
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId) || !Guid.TryParse(userId, out var guid))
        {
            return Results.Unauthorized();
        }

        command.CreatedByUserId = guid;
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Created($"/api/v1/auctions/{result.Data}", result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> GetAuctionsAsync(
        IQueryProcessor queryProcessor,
        string? status = null,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        // Bind status as a string and parse case-insensitively so REST
        // clients can use the conventional lower-case spelling
        // (?status=active) rather than the C# enum member name. Invalid
        // values produce a typed 400 with the canonical list.
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<AuctionStatus>(status, ignoreCase: true, out var parsed) ||
                !Enum.IsDefined(typeof(AuctionStatus), parsed))
            {
                return Results.BadRequest(BaseResult.Fail(
                    $"Invalid status '{status}'. Allowed values: {string.Join(", ", Enum.GetNames<AuctionStatus>())}."));
            }

            var filteredQuery = new GetAuctionsByStatusQuery
            {
                AuctionStatus = parsed,
                Page = page,
                PageSize = pageSize
            };
            var filteredResult = await queryProcessor.ExecuteAsync(filteredQuery, cancellationToken);
            return Results.Ok(filteredResult);
        }

        var query = new GetAuctionsQuery { Page = page, PageSize = pageSize };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetActiveAuctionsAsync(
        IQueryProcessor queryProcessor,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetActiveAuctionsQuery { Page = page, PageSize = pageSize };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetAuctionByIdAsync(
        IQueryProcessor queryProcessor,
        HttpContext httpContext,
        Guid id,
        CancellationToken cancellationToken)
    {
        var query = new GetAuctionByIdQuery { AuctionId = id };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        if (!result.Success)
        {
            return Results.NotFound(result);
        }

        // Short-lived cache hint for the auction detail endpoint. Auction
        // state changes on every accepted bid, so the TTL has to stay
        // small; the value chosen here is only meant to absorb retry
        // bursts from a single client.
        httpContext.Response.Headers.CacheControl = "private, max-age=2";
        return Results.Ok(result);
    }

    private static async Task<IResult> UpdateAuctionAsync(
        IAmACommandProcessor commandProcessor,
        Guid id,
        UpdateAuctionCommand command,
        CancellationToken cancellationToken)
    {
        command.AuctionId = id;
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> CloseAuctionAsync(
        IAmACommandProcessor commandProcessor,
        Guid id,
        CancellationToken cancellationToken)
    {
        var command = new CloseAuctionCommand { AuctionId = id };
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> CancelAuctionAsync(
        IAmACommandProcessor commandProcessor,
        Guid id,
        string? reason,
        CancellationToken cancellationToken)
    {
        var command = new CancelAuctionCommand { AuctionId = id, Reason = reason };
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }
}
