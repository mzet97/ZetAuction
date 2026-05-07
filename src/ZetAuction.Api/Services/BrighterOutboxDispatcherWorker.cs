using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Paramore.Brighter;
using Paramore.Brighter.Outbox.PostgreSql;
using ZetAuction.Application.Common.Observability;
using ZetAuction.Infrastructure.Persistence;
using ZetAuction.Shared.Domain;

namespace ZetAuction.Api.Services;

/// <summary>
/// Background worker that drains Brighter's <see cref="PostgreSqlOutbox"/>
/// and re-publishes each <see cref="Message"/> through the in-process
/// <see cref="IAmACommandProcessor"/>. Replaces the previous custom
/// <c>OutboxDispatcherWorker</c> now that storage is owned by Brighter.
/// </summary>
/// <remarks>
/// <para>
/// This is the consumer half of the transactional outbox. The producer
/// half is <c>DomainEventsSaveChangesInterceptor</c>, which writes a
/// Brighter <see cref="Message"/> in the same DB transaction as the
/// aggregate change. We poll the outbox via
/// <c>OutstandingMessagesAsync</c>, deserialize the payload back to the
/// concrete .NET event type using the <c>clr_type</c> header, and dispatch
/// through <see cref="IAmACommandProcessor.PublishAsync{T}"/> so the
/// existing in-process handlers (<c>BidPlacedEventHandler</c>, etc.)
/// receive it.
/// </para>
/// <para>
/// <b>Idempotency contract (enforced):</b> Brighter v9.9.13 does not lock
/// outstanding rows the way the previous custom implementation did with
/// <c>FOR UPDATE SKIP LOCKED</c>. Multiple replicas of this worker may pick
/// the same row up before <c>MarkDispatchedAsync</c> commits. Before
/// dispatch we do an atomic claim against the <c>processed_events</c>
/// table (<c>INSERT ... ON CONFLICT DO NOTHING</c>) so only one replica
/// actually invokes <c>PublishAsync&lt;T&gt;</c> per message. The losing
/// replica still calls <c>MarkDispatchedAsync</c>, so the outbox row
/// drops out of polling regardless. Handler authors do not need to
/// implement their own dedup — see ADR-0003.
/// </para>
/// <para>
/// <b>Poison-message protection:</b> Brighter v9.9.13 has no
/// <c>FailureCount</c> column. To prevent a permanently-undeliverable
/// message from looping forever, we track failure counts in memory; once
/// a message exceeds <see cref="PoisonRetryThreshold"/>, we mark it as
/// dispatched (so it leaves the outstanding set) and emit an Error log +
/// the <c>zetauction_outbox_dead_lettered_total</c> counter. Operators
/// triage from logs (see runbook outbox-stuck-messages.md).
/// </para>
/// </remarks>
public sealed class BrighterOutboxDispatcherWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private const int BatchSize = 50;
    private const int PoisonRetryThreshold = 10;

    // Wait this long after a message lands before we consider it
    // "outstanding" enough to dispatch. Zero is safe because the producer
    // already committed the row inside the aggregate transaction; we are
    // not racing the producer.
    private const double MillisecondsBeforeOutstanding = 0;

    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // Cached open generic MethodInfo for IAmACommandProcessor.PublishAsync<T>;
    // the closed generics are computed lazily once per concrete event type.
    // Without this cache we'd hit GetMethods()+First()+MakeGenericMethod on
    // every message dispatched.
    private static readonly MethodInfo OpenPublishAsyncMethod = typeof(IAmACommandProcessor)
        .GetMethods()
        .First(m => m.Name == nameof(IAmACommandProcessor.PublishAsync)
                    && m.IsGenericMethodDefinition);

    private static readonly ConcurrentDictionary<Type, MethodInfo> ClosedPublishAsyncMethods = new();

    // In-memory failure tracking for poison-message detection. Keys are
    // outbox MessageIds; values are consecutive failure counts. Reset
    // when the message is dead-lettered (so memory does not grow
    // unbounded — the row is removed from the outstanding set).
    private readonly ConcurrentDictionary<Guid, int> _failureCounts = new();

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BrighterOutboxDispatcherWorker> _logger;

    public BrighterOutboxDispatcherWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<BrighterOutboxDispatcherWorker> logger)
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
                var processed = await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
                if (processed == 0)
                {
                    await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Brighter outbox dispatcher iteration failed");
                await Task.Delay(PollInterval, stoppingToken).ConfigureAwait(false);
            }
        }
    }

    private async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<PostgreSqlOutbox>();
        var dbContext = scope.ServiceProvider.GetRequiredService<ZetAuctionDbContext>();
        var commandProcessor = scope.ServiceProvider.GetRequiredService<IAmACommandProcessor>();
        var metrics = scope.ServiceProvider.GetRequiredService<IAuctionMetrics>();

        var outstanding = (await outbox.OutstandingMessagesAsync(
                MillisecondsBeforeOutstanding,
                pageSize: BatchSize,
                pageNumber: 1,
                args: null,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false)).ToList();

        if (outstanding.Count == 0)
        {
            return 0;
        }

        foreach (var message in outstanding)
        {
            await DispatchSingleAsync(outbox, dbContext, commandProcessor, metrics, message, cancellationToken)
                .ConfigureAwait(false);
        }

        _logger.LogDebug("Brighter outbox dispatcher processed {Count} messages", outstanding.Count);
        return outstanding.Count;
    }

    private async Task DispatchSingleAsync(
        PostgreSqlOutbox outbox,
        ZetAuctionDbContext dbContext,
        IAmACommandProcessor commandProcessor,
        IAuctionMetrics metrics,
        Message message,
        CancellationToken cancellationToken)
    {
        try
        {
            // Poison short-circuit. If a previous attempt failed enough
            // times we've already dead-lettered (MarkDispatched) it, but
            // a slow concurrent reader may still see it; keep the guard
            // just in case the in-memory state survives a restart that
            // dispatched the row in another replica.
            if (_failureCounts.TryGetValue(message.Id, out var existing) &&
                existing >= PoisonRetryThreshold)
            {
                return;
            }

            var eventType = ResolveAndValidateEventType(message);
            var domainEvent = JsonSerializer.Deserialize(message.Body.Value, eventType, PayloadJsonOptions)
                ?? throw new InvalidOperationException(
                    $"Failed to deserialize outbox message {message.Id} payload as {eventType.FullName}.");

            // Atomic cross-replica claim: only one replica wins the
            // INSERT, the others see 0 rows affected and skip the actual
            // PublishAsync — the handler runs at most once per MessageId.
            var rowsClaimed = await dbContext.Database
                .ExecuteSqlInterpolatedAsync(
                    $@"INSERT INTO processed_events (event_id, processed_at)
                       VALUES ({message.Id}, NOW())
                       ON CONFLICT (event_id) DO NOTHING",
                    cancellationToken)
                .ConfigureAwait(false);

            if (rowsClaimed == 1)
            {
                await PublishDynamicAsync(commandProcessor, domainEvent, eventType, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                // Another replica already invoked the handler chain for
                // this MessageId. We just record the duplicate and let
                // MarkDispatched run so the row leaves the outstanding
                // set on this replica too.
                metrics.RecordOutboxDuplicateSkipped();
                _logger.LogDebug(
                    "Outbox message {MessageId} (topic {Topic}) was already claimed by another replica; skipping PublishAsync",
                    message.Id, message.Header.Topic);
            }

            await outbox.MarkDispatchedAsync(message.Id, DateTime.UtcNow,
                    args: null, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            metrics.RecordOutboxDispatched();
            _failureCounts.TryRemove(message.Id, out _);
        }
        catch (Exception ex)
        {
            var failures = _failureCounts.AddOrUpdate(message.Id, 1, (_, prev) => prev + 1);
            metrics.RecordOutboxFailed();

            if (failures >= PoisonRetryThreshold)
            {
                // Dead-letter: mark dispatched so the row leaves the
                // outstanding set, log loudly, and surface a counter the
                // operator can alert on. The row itself is preserved in
                // outbox_messages with Dispatched populated, so the
                // payload survives for forensic inspection (see
                // runbooks/outbox-stuck-messages.md for triage steps).
                metrics.RecordOutboxDeadLettered();
                _logger.LogError(
                    ex,
                    "Outbox message {MessageId} (topic {Topic}) exceeded {Threshold} dispatch attempts; dead-lettering",
                    message.Id, message.Header.Topic, PoisonRetryThreshold);

                try
                {
                    await outbox.MarkDispatchedAsync(message.Id, DateTime.UtcNow,
                            args: null, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (Exception markEx)
                {
                    _logger.LogError(
                        markEx,
                        "Failed to mark dead-lettered outbox message {MessageId} as dispatched; will retry next iteration",
                        message.Id);
                }
            }
            else
            {
                _logger.LogWarning(
                    ex,
                    "Outbox dispatch failed for message {MessageId} (topic {Topic}), attempt {Attempt}/{Threshold}; will retry next iteration",
                    message.Id, message.Header.Topic, failures, PoisonRetryThreshold);
            }
        }
    }

    private static Type ResolveAndValidateEventType(Message message)
    {
        if (!message.Header.Bag.TryGetValue("clr_type", out var rawClrType) ||
            rawClrType is not string clrTypeName ||
            string.IsNullOrWhiteSpace(clrTypeName))
        {
            throw new InvalidOperationException(
                $"Outbox message {message.Id} is missing the 'clr_type' header — cannot resolve the .NET event type.");
        }

        var eventType = Type.GetType(clrTypeName, throwOnError: false);
        if (eventType is null)
        {
            throw new InvalidOperationException(
                $"Cannot resolve message type '{clrTypeName}' for outbox message {message.Id}. " +
                "The runtime assembly may be out of sync with the database.");
        }

        // Whitelist: anything resolvable through Type.GetType could in
        // principle be loaded from the running assemblies. By gating on
        // IDomainEvent we make sure a tampered outbox row cannot trick
        // the dispatcher into instantiating arbitrary classes.
        if (!typeof(IDomainEvent).IsAssignableFrom(eventType))
        {
            throw new InvalidOperationException(
                $"Resolved type '{clrTypeName}' for outbox message {message.Id} does not implement IDomainEvent.");
        }

        return eventType;
    }

    private static async Task PublishDynamicAsync(
        IAmACommandProcessor commandProcessor,
        object domainEvent,
        Type eventType,
        CancellationToken cancellationToken)
    {
        // Brighter v9 PublishAsync<T>(T, bool, CancellationToken) — the
        // RequestContext parameter from v10 does not exist on this version.
        var generic = ClosedPublishAsyncMethods.GetOrAdd(
            eventType,
            t => OpenPublishAsyncMethod.MakeGenericMethod(t));

        var task = (Task?)generic.Invoke(commandProcessor, new object?[]
        {
            domainEvent,
            false, // continueOnCapturedContext
            cancellationToken
        });

        if (task is not null)
        {
            await task.ConfigureAwait(false);
        }
    }
}
