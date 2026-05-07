using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using Paramore.Brighter;
using Paramore.Brighter.Outbox.PostgreSql;
using ZetAuction.Infrastructure.Persistence.Brighter;
using ZetAuction.Shared.Domain;

namespace ZetAuction.Infrastructure.Persistence.Interceptors;

/// <summary>
/// SaveChanges interceptor that performs two responsibilities atomically
/// with the surrounding transaction:
///
/// 1. <b>Audit fields</b> — populates <see cref="IAuditable.CreatedAtUtc"/>
///    and <see cref="IAuditable.UpdatedAtUtc"/> on tracked entities.
///
/// 2. <b>Transactional outbox via Brighter</b> — drains domain events from
///    every tracked aggregate root and writes them through Brighter's
///    <see cref="PostgreSqlOutbox"/> using the active EF Core
///    <see cref="EntityFrameworkPostgreSqlConnectionProvider"/>. The outbox
///    INSERT and the aggregate state change land in the same Postgres
///    transaction, giving the standard dual-write guarantee: either both
///    survive or neither does. The async <c>BrighterOutboxDispatcherWorker</c>
///    later re-publishes each <see cref="Message"/> through the in-process
///    <see cref="IAmACommandProcessor"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Atomicity contract:</b> the outbox INSERT must share a transaction with
/// the aggregate writes. If the calling handler already opened one via
/// <c>IUnitOfWork.BeginTransactionAsync</c> (e.g.
/// <c>PlaceBidCommandHandler</c>), we just join it. If it did not (handlers
/// that only call <c>SaveChangesAsync</c>), we open one ourselves here and
/// commit / roll back through the corresponding <c>SavedChangesAsync</c> /
/// <c>SaveChangesFailedAsync</c> hooks. Without this, EF's implicit
/// transaction would not be visible to the connection provider, and the
/// outbox INSERT would either fail (Npgsql refuses commands without a
/// transaction binding when the connection has one open) or break atomicity.
/// </para>
/// </remarks>
public sealed class DomainEventsSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    // Transactions we begin ourselves are stashed per-DbContext so the
    // matching SavedChangesAsync / SaveChangesFailedAsync can finish them.
    // ConditionalWeakTable prevents leaking a strong reference to the
    // DbContext across the GC boundary if Save... never fires.
    private static readonly ConditionalWeakTable<DbContext, IDbContextTransaction> InterceptorOwnedTransactions = new();

    private readonly PostgreSqlOutboxConfiguration _outboxConfiguration;

    public DomainEventsSaveChangesInterceptor(PostgreSqlOutboxConfiguration outboxConfiguration)
    {
        _outboxConfiguration = outboxConfiguration;
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            AutoPopulateAuditFields(eventData.Context);
            await EnqueueDomainEventsToOutboxAsync(eventData.Context, cancellationToken)
                .ConfigureAwait(false);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null &&
            InterceptorOwnedTransactions.TryGetValue(eventData.Context, out var ownedTx))
        {
            InterceptorOwnedTransactions.Remove(eventData.Context);
            try
            {
                await ownedTx.CommitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                await ownedTx.DisposeAsync().ConfigureAwait(false);
            }
        }

        return await base.SavedChangesAsync(eventData, result, cancellationToken)
            .ConfigureAwait(false);
    }

    public override async Task SaveChangesFailedAsync(
        DbContextErrorEventData eventData,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null &&
            InterceptorOwnedTransactions.TryGetValue(eventData.Context, out var ownedTx))
        {
            InterceptorOwnedTransactions.Remove(eventData.Context);
            try
            {
                await ownedTx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Swallow — the original SaveChanges failure is what the
                // caller needs to see; rollback errors layered on top of
                // that just confuse the stack trace.
            }
            finally
            {
                await ownedTx.DisposeAsync().ConfigureAwait(false);
            }
        }

        await base.SaveChangesFailedAsync(eventData, cancellationToken).ConfigureAwait(false);
    }

    private static void AutoPopulateAuditFields(DbContext context)
    {
        var utcNow = DateTime.UtcNow;

        foreach (var entry in context.ChangeTracker.Entries()
            .Where(e => e.Entity is IAuditable &&
                        e.State is EntityState.Added or EntityState.Modified))
        {
            var auditable = (IAuditable)entry.Entity;

            if (entry.State == EntityState.Added)
            {
                auditable.GetType().GetProperty(nameof(IAuditable.CreatedAtUtc))?
                    .SetValue(auditable, utcNow);
                auditable.GetType().GetProperty(nameof(IAuditable.UpdatedAtUtc))?
                    .SetValue(auditable, utcNow);
            }
            else if (entry.State == EntityState.Modified)
            {
                auditable.GetType().GetProperty(nameof(IAuditable.UpdatedAtUtc))?
                    .SetValue(auditable, utcNow);
            }
        }
    }

    private async Task EnqueueDomainEventsToOutboxAsync(DbContext context, CancellationToken cancellationToken)
    {
        var aggregatesWithEvents = context.ChangeTracker.Entries()
            .Where(entry => entry.Entity is IHasDomainEvents withEvents && withEvents.DomainEvents.Count > 0)
            .Select(entry => (IHasDomainEvents)entry.Entity)
            .ToList();

        if (aggregatesWithEvents.Count == 0)
        {
            return;
        }

        // Atomicity contract: domain events ride the same DB transaction as
        // the aggregate change. If the caller already opened one (via
        // IUnitOfWork.BeginTransactionAsync) we join it; if not we own one
        // ourselves and commit / rollback through the SavedChangesAsync /
        // SaveChangesFailedAsync hooks. Either way, the outbox INSERT and
        // the EF SaveChanges land in the same Postgres transaction.
        if (context.Database.CurrentTransaction is null)
        {
            var ownedTx = await context.Database.BeginTransactionAsync(cancellationToken)
                .ConfigureAwait(false);
            InterceptorOwnedTransactions.Add(context, ownedTx);
        }

        // Both the outbox and the transactionConnectionProvider arg need an
        // IPostgreSqlConnectionProvider tied to the EF Core DbContext we're
        // intercepting; one instance fulfils both roles.
        var connectionProvider = new EntityFrameworkPostgreSqlConnectionProvider((ZetAuctionDbContext)context);
        var outbox = new PostgreSqlOutbox(_outboxConfiguration, connectionProvider);

        foreach (var aggregate in aggregatesWithEvents)
        {
            var aggregateId = TryGetAggregateId(aggregate);
            var aggregateType = aggregate.GetType().Name;

            foreach (var domainEvent in aggregate.DomainEvents)
            {
                var message = MapToBrighterMessage(domainEvent, aggregateType, aggregateId);

                await outbox.AddAsync(
                        message,
                        outBoxTimeout: -1,
                        cancellationToken: cancellationToken,
                        transactionConnectionProvider: connectionProvider)
                    .ConfigureAwait(false);
            }

            aggregate.ClearDomainEvents();
        }
    }

    private static Message MapToBrighterMessage(
        IDomainEvent domainEvent,
        string aggregateType,
        Guid? aggregateId)
    {
        var eventType = domainEvent.GetType();
        // Topic is the canonical Brighter routing key. Using the .NET FQN
        // gives us a stable name without forcing a separate topic registry —
        // works because publish/subscribe is in-process only.
        var topic = eventType.FullName ?? eventType.Name;
        var payload = JsonSerializer.Serialize(domainEvent, eventType, PayloadJsonOptions);

        var header = new MessageHeader(
            messageId: domainEvent.EventId,
            topic: topic,
            messageType: MessageType.MT_EVENT,
            correlationId: null,
            replyTo: string.Empty,
            contentType: MessageBody.APPLICATION_JSON);
        header.TimeStamp = domainEvent.OccurredOnUtc == default ? DateTime.UtcNow : domainEvent.OccurredOnUtc;

        // Stash the assembly-qualified name so the dispatcher worker can
        // deserialize the body back to the strongly typed event without a
        // type registry. Aggregate metadata is purely diagnostic.
        header.Bag["clr_type"] = eventType.AssemblyQualifiedName ?? eventType.FullName!;
        header.Bag["aggregate_type"] = aggregateType;
        if (aggregateId.HasValue)
        {
            header.Bag["aggregate_id"] = aggregateId.Value.ToString();
        }

        var body = new MessageBody(payload, MessageBody.APPLICATION_JSON);
        return new Message(header, body);
    }

    private static Guid? TryGetAggregateId(object aggregate)
    {
        var idProperty = aggregate.GetType().GetProperty("Id");
        var value = idProperty?.GetValue(aggregate);
        return value is Guid guid ? guid : null;
    }
}
