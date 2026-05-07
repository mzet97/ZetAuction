using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Exceptions;

namespace ZetAuction.Api.Middleware;

/// <summary>
/// Translates exceptions raised by the pipeline into RFC 7807 ProblemDetails
/// responses. Replaces the legacy ExceptionHandlingMiddleware that returned
/// an ad-hoc <c>{ statusCode, message, traceId }</c> shape and leaked raw
/// exception text via <c>Results.Problem(ex.Message)</c> at the endpoint
/// level. Domain exceptions become 4xx with stable <c>type</c> URIs that
/// clients can program against; everything else is a generic 500 with no
/// internal detail leaking out.
/// </summary>
public sealed class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private const string ProblemBaseUri = "https://zetauction.dev/problems/";

    private readonly IProblemDetailsService _problemDetailsService;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(
        IProblemDetailsService problemDetailsService,
        ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _problemDetailsService = problemDetailsService;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var problem = MapException(exception);

        if (problem.Status >= 500)
        {
            _logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            _logger.LogWarning(exception, "Handled domain failure on {Method} {Path}: {Title}", httpContext.Request.Method, httpContext.Request.Path, problem.Title);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        return await _problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    private static ProblemDetails MapException(Exception exception)
    {
        return exception switch
        {
            InsufficientBidAmountException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "insufficient-bid-amount",
                Title = "Insufficient bid amount",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ex.Message,
                Extensions =
                {
                    ["bidAmount"] = ex.BidAmount,
                    ["requiredAmount"] = ex.CurrentPrice
                }
            },
            AuctionClosedException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "auction-closed",
                Title = "Auction closed",
                Status = StatusCodes.Status409Conflict,
                Detail = ex.Message,
                Extensions = { ["auctionId"] = ex.AuctionId }
            },
            InvalidBidException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "invalid-bid",
                Title = "Invalid bid",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ex.Message
            },
            // ConcurrencyConflictException must come before the generic
            // DomainException arm because it inherits from it; otherwise the
            // catch-all would shadow the more specific 409 mapping.
            ConcurrencyConflictException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "concurrency-conflict",
                Title = "Concurrency conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = "The resource was modified by another request. Retry with the latest version.",
                Extensions =
                {
                    ["aggregateName"] = ex.AggregateName,
                    ["aggregateId"] = ex.AggregateId
                }
            },
            DomainException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "domain-rule-violation",
                Title = "Domain rule violation",
                Status = StatusCodes.Status422UnprocessableEntity,
                Detail = ex.Message
            },
            DbUpdateConcurrencyException => new ProblemDetails
            {
                Type = ProblemBaseUri + "concurrency-conflict",
                Title = "Concurrency conflict",
                Status = StatusCodes.Status409Conflict,
                Detail = "The resource was modified by another request. Retry with the latest version."
            },
            UnauthorizedAccessException => new ProblemDetails
            {
                Type = ProblemBaseUri + "unauthorized",
                Title = "Unauthorized",
                Status = StatusCodes.Status401Unauthorized,
                Detail = "The request requires authentication."
            },
            KeyNotFoundException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "not-found",
                Title = "Resource not found",
                Status = StatusCodes.Status404NotFound,
                Detail = ex.Message
            },
            ArgumentException ex => new ProblemDetails
            {
                Type = ProblemBaseUri + "invalid-argument",
                Title = "Invalid argument",
                Status = StatusCodes.Status400BadRequest,
                Detail = ex.Message
            },
            _ => new ProblemDetails
            {
                Type = ProblemBaseUri + "internal-error",
                Title = "Internal server error",
                Status = StatusCodes.Status500InternalServerError,
                Detail = "An unexpected error occurred while processing the request."
            }
        };
    }
}
