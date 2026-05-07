using Paramore.Brighter;
using Paramore.Darker;
using ZetAuction.Api.Common;
using ZetAuction.Application.Common.Messaging;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Application.Users.Queries;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Api.Endpoints;

public sealed class UserEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/users", CreateUserAsync)
            .WithName("CreateUser")
            .WithTags("Users")
            .WithSummary("Register a new user")
            .Produces<BaseResult<Guid>>(StatusCodes.Status201Created)
            .Produces<BaseResult>(StatusCodes.Status400BadRequest);

        endpoints.MapGet("/api/v1/users", GetUsersAsync)
            .WithName("GetUsers")
            .WithTags("Users")
            .WithSummary("Get a paginated list of users")
            .Produces<BaseResultList<UserViewModel>>()
            .RequireAuthorization();

        endpoints.MapGet("/api/v1/users/{id:guid}", GetUserByIdAsync)
            .WithName("GetUserById")
            .WithTags("Users")
            .WithSummary("Get a user by identifier")
            .Produces<BaseResult<UserViewModel>>()
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization();

        endpoints.MapPut("/api/v1/users/{id:guid}", UpdateUserAsync)
            .WithName("UpdateUser")
            .WithTags("Users")
            .WithSummary("Update an existing user's profile")
            .Produces<BaseResult>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();

        endpoints.MapDelete("/api/v1/users/{id:guid}", DeleteUserAsync)
            .WithName("DeleteUser")
            .WithTags("Users")
            .WithSummary("Delete a user by identifier")
            .Produces<BaseResult>()
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized)
            .RequireAuthorization();
    }

    private static async Task<IResult> CreateUserAsync(
        IAmACommandProcessor commandProcessor,
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Created($"/api/v1/users/{result.Data}", result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> GetUsersAsync(
        IQueryProcessor queryProcessor,
        int page = 1,
        int pageSize = 10,
        CancellationToken cancellationToken = default)
    {
        var query = new GetAllUsersQuery { Page = page, PageSize = pageSize };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        return Results.Ok(result);
    }

    private static async Task<IResult> GetUserByIdAsync(
        IQueryProcessor queryProcessor,
        Guid id,
        CancellationToken cancellationToken)
    {
        var query = new GetUserByIdQuery { UserId = id };
        var result = await queryProcessor.ExecuteAsync(query, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.NotFound(result);
    }

    private static async Task<IResult> UpdateUserAsync(
        IAmACommandProcessor commandProcessor,
        Guid id,
        UpdateUserCommand command,
        CancellationToken cancellationToken)
    {
        command.UserId = id;
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> DeleteUserAsync(
        IAmACommandProcessor commandProcessor,
        Guid id,
        CancellationToken cancellationToken)
    {
        var command = new DeleteUserCommand { UserId = id };
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.NotFound(result);
    }
}
