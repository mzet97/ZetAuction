using Microsoft.AspNetCore.RateLimiting;
using Paramore.Brighter;
using ZetAuction.Api.Common;
using ZetAuction.Api.Configuration;
using ZetAuction.Application.Auth.Commands;
using ZetAuction.Application.Auth.Responses;
using ZetAuction.Application.Common.Messaging;
using ZetAuction.Application.Users.Commands;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Api.Endpoints;

public sealed class AuthEndpoints : IEndpoint
{
    public static void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/auth/register", RegisterAsync)
            .WithName("Register")
            .WithTags("Authentication")
            .WithSummary("Register a new user account")
            .WithDescription("Creates a user with a hashed password. Returns the new user identifier on success.")
            .RequireRateLimiting(RateLimitingConfig.AuthPolicy)
            .Produces<BaseResult<Guid>>(StatusCodes.Status201Created)
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);

        endpoints.MapPost("/api/v1/auth/login", LoginAsync)
            .WithName("Login")
            .WithTags("Authentication")
            .WithSummary("Authenticate a user and return a JWT")
            .WithDescription("Validates the credentials and issues a signed JWT to be presented as Bearer on every protected endpoint.")
            .RequireRateLimiting(RateLimitingConfig.AuthPolicy)
            .Produces<BaseResult<AuthResponse>>()
            .Produces<BaseResult>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status429TooManyRequests);
    }

    private static async Task<IResult> RegisterAsync(
        IAmACommandProcessor commandProcessor,
        CreateUserCommand command,
        CancellationToken cancellationToken)
    {
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Created($"/api/v1/users/{result.Data}", result)
            : Results.BadRequest(result);
    }

    private static async Task<IResult> LoginAsync(
        IAmACommandProcessor commandProcessor,
        LoginCommand command,
        CancellationToken cancellationToken)
    {
        var result = await commandProcessor.SendWithResultAsync(command, cancellationToken);

        return result.Success
            ? Results.Ok(result)
            : Results.BadRequest(result);
    }
}
