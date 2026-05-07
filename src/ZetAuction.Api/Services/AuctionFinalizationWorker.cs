using ZetAuction.Application.Common.Observability;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Services;

namespace ZetAuction.Api.Services;

/// <summary>
/// Periodic background worker that finalises auctions whose
/// <c>EndDate</c> has elapsed. The implementation relies on Postgres
/// <c>FOR UPDATE SKIP LOCKED</c> via
/// <see cref="IAuctionRepository.LockDueForFinalizationAsync"/> so any
/// number of API replicas can run this worker without coordination
/// — each replica claims its own batch and the others see the locked
/// rows as "already taken".
/// </summary>
/// <remarks>
/// This worker no longer dispatches domain events itself. The
/// <c>DomainEventsSaveChangesInterceptor</c> writes the
/// <c>AuctionClosedEvent</c> into the transactional outbox during
/// <c>SaveChangesAsync</c>, and the dedicated <c>OutboxDispatcherWorker</c>
/// publishes them via Brighter. That separation gives at-least-once
/// delivery even if the API process crashes between commit and publish.
/// </remarks>
public sealed class AuctionFinalizationWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private const int BatchSize = 100;

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AuctionFinalizationWorker> _logger;

    public AuctionFinalizationWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<AuctionFinalizationWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await FinalizeBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Auction finalization worker iteration failed");
                await Task.Delay(PollInterval, stoppingToken);
            }
        }
    }

    private async Task<int> FinalizeBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var dateTimeProvider = scope.ServiceProvider.GetRequiredService<IDateTimeProvider>();
        var metrics = scope.ServiceProvider.GetRequiredService<IAuctionMetrics>();
        var now = dateTimeProvider.UtcNow;

        await unitOfWork.BeginTransactionAsync();

        try
        {
            var dueAuctions = await unitOfWork.Auctions.LockDueForFinalizationAsync(
                now, BatchSize, cancellationToken);

            if (dueAuctions.Count == 0)
            {
                await unitOfWork.RollbackAsync();
                return 0;
            }

            foreach (var auction in dueAuctions)
            {
                auction.Close(now);
                auction.MarkUpdated(now);
                await unitOfWork.Auctions.UpdateAsync(auction);
                metrics.RecordAuctionFinalized();
            }

            await unitOfWork.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Auction finalization batch completed: {Count} auctions finalised",
                dueAuctions.Count);

            return dueAuctions.Count;
        }
        catch
        {
            await unitOfWork.RollbackAsync();
            throw;
        }
    }
}
