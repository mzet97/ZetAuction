using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using Polly;
using Polly.Retry;
using ZetAuction.Application.Bids.ViewModels;
using ZetAuction.Application.Common.Observability;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Bids.Commands;

public class PlaceBidCommandHandler : RequestHandlerAsync<PlaceBidCommand>
{
    private const int MaxConcurrencyRetries = 3;

    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<PlaceBidCommandHandler> _logger;
    private readonly IRateLimiterService _rateLimiterService;
    private readonly IBidCacheService? _bidCacheService;
    private readonly IAuctionMetrics _metrics;
    private readonly ResiliencePipeline _concurrencyRetryPipeline;

    public PlaceBidCommandHandler(
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILogger<PlaceBidCommandHandler> logger)
        : this(unitOfWork, dateTimeProvider, logger, new AllowAllRateLimiterService(), null, NoopAuctionMetrics.Instance)
    {
    }

    public PlaceBidCommandHandler(
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILogger<PlaceBidCommandHandler> logger,
        IRateLimiterService rateLimiterService,
        IBidCacheService? bidCacheService = null,
        IAuctionMetrics? metrics = null)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
        _rateLimiterService = rateLimiterService;
        _bidCacheService = bidCacheService;
        _metrics = metrics ?? NoopAuctionMetrics.Instance;

        _concurrencyRetryPipeline = new ResiliencePipelineBuilder()
            .AddRetry(new RetryStrategyOptions
            {
                ShouldHandle = new PredicateBuilder().Handle<ConcurrencyConflictException>(),
                MaxRetryAttempts = MaxConcurrencyRetries,
                BackoffType = DelayBackoffType.Exponential,
                Delay = TimeSpan.FromMilliseconds(20),
                UseJitter = true,
                OnRetry = args =>
                {
                    _metrics.RecordConcurrencyConflict();
                    _logger.LogWarning(
                        args.Outcome.Exception,
                        "Concurrency conflict on bid placement; attempt {Attempt}/{Max} will retry after {Delay}ms",
                        args.AttemptNumber + 1,
                        MaxConcurrencyRetries,
                        args.RetryDelay.TotalMilliseconds);
                    return ValueTask.CompletedTask;
                }
            })
            .Build();
    }

    public override async Task<PlaceBidCommand> HandleAsync(PlaceBidCommand command, CancellationToken cancellationToken = default)
    {
        var rateLimitKey = $"user:{command.UserId}:bids";
        if (!await _rateLimiterService.IsAllowedAsync(rateLimitKey, 5, 300))
        {
            _metrics.RecordRateLimitHit();
            _metrics.RecordBidRejected("rate_limited");
            _logger.LogWarning("Bid placement failed: rate limit exceeded for user {UserId}", command.UserId);
            command.Result = BaseResult.Fail("Rate limit exceeded. Maximum 5 bids per 5 minutes.");
            return await base.HandleAsync(command, cancellationToken);
        }

        var stopwatch = Stopwatch.StartNew();
        BidPlacementOutcome? outcome = null;
        await _concurrencyRetryPipeline.ExecuteAsync(async ct =>
        {
            outcome = await TryPlaceBidAsync(command, ct);
        }, cancellationToken);

        if (outcome is null)
        {
            _metrics.RecordBidRejected("unknown");
            command.Result = BaseResult.Fail("Bid placement failed unexpectedly.");
            return await base.HandleAsync(command, cancellationToken);
        }

        if (outcome.Bid is not null)
        {
            stopwatch.Stop();
            _metrics.RecordBidPlaced();
            _metrics.RecordBidPlacementDuration(stopwatch.Elapsed.TotalMilliseconds);
            await UpdateCacheAsync(command, outcome);
        }
        else
        {
            _metrics.RecordBidRejected(outcome.RejectionReason ?? "domain");
        }

        command.Result = outcome.Result;
        return await base.HandleAsync(command, cancellationToken);
    }

    private async Task<BidPlacementOutcome> TryPlaceBidAsync(PlaceBidCommand command, CancellationToken cancellationToken)
    {
        await _unitOfWork.ResetTrackingAsync();
        await _unitOfWork.BeginTransactionAsync();

        try
        {
            var auction = await _unitOfWork.Auctions.GetByIdWithBidsAsync(command.AuctionId, cancellationToken);
            if (auction is null)
            {
                await _unitOfWork.RollbackAsync();
                _logger.LogWarning("Bid placement failed: auction {AuctionId} not found", command.AuctionId);
                return BidPlacementOutcome.Failed(BaseResult.Fail("Auction not found."), "not_found");
            }

            if (auction.Status != AuctionStatus.Active)
            {
                await _unitOfWork.RollbackAsync();
                _logger.LogWarning(
                    "Bid placement failed: auction {AuctionId} is not active (status: {Status})",
                    command.AuctionId,
                    auction.Status);
                return BidPlacementOutcome.Failed(BaseResult.Fail(
                    $"Cannot place bid. Auction is currently '{auction.Status}' and not accepting bids."),
                    "not_active");
            }

            var bid = auction.PlaceBid(command.UserId, command.Amount, _dateTimeProvider);
            bid.MarkCreated(_dateTimeProvider.UtcNow);

            await _unitOfWork.Auctions.UpdateAsync(auction);
            // CommitAsync triggers DomainEventsSaveChangesInterceptor, which
            // drains BidPlacedEvent into the outbox table inside this same
            // transaction. The async OutboxDispatcherWorker delivers it via
            // Brighter — handler no longer publishes directly.
            await _unitOfWork.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Bid placed successfully on auction {AuctionId} by user {UserId} for amount {Amount}",
                command.AuctionId,
                command.UserId,
                command.Amount);

            return BidPlacementOutcome.Succeeded(auction, bid);
        }
        catch (ConcurrencyConflictException)
        {
            // CommitAsync has already rolled back; rethrow so Polly applies
            // the configured backoff and runs another attempt.
            throw;
        }
        catch (DomainException ex)
        {
            await _unitOfWork.RollbackAsync();
            _logger.LogWarning(
                ex,
                "Bid placement rejected for auction {AuctionId} by user {UserId}: {Reason}",
                command.AuctionId,
                command.UserId,
                ex.Message);
            throw;
        }
    }

    private async Task UpdateCacheAsync(PlaceBidCommand command, BidPlacementOutcome outcome)
    {
        if (_bidCacheService is null || outcome.Bid is null)
        {
            return;
        }

        var user = await _unitOfWork.Users.GetByIdAsync(command.UserId);
        await _bidCacheService.SetHighestBidAsync(command.AuctionId, new BidViewModel
        {
            Id = outcome.Bid.Id,
            AuctionId = outcome.Bid.AuctionId,
            UserId = outcome.Bid.UserId,
            Amount = outcome.Bid.Amount,
            PlacedAtUtc = outcome.Bid.PlacedAtUtc,
            UserName = user?.Name ?? string.Empty
        });
    }

    private sealed record BidPlacementOutcome(BaseResult Result, Auction? Auction, Bid? Bid, string? RejectionReason = null)
    {
        public static BidPlacementOutcome Succeeded(Auction auction, Bid bid) =>
            new(BaseResult.Ok("Bid placed successfully."), auction, bid);

        public static BidPlacementOutcome Failed(BaseResult result, string? reason = null) =>
            new(result, null, null, reason);
    }

    private sealed class AllowAllRateLimiterService : IRateLimiterService
    {
        public Task<bool> IsAllowedAsync(string key, int maxRequests, int windowSeconds)
        {
            return Task.FromResult(true);
        }
    }
}
