using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Paramore.Brighter;
using Paramore.Brighter.PostgreSql;

namespace ZetAuction.Infrastructure.Persistence.Brighter;

/// <summary>
/// Bridges Brighter's <see cref="PostgreSqlOutbox"/> to the active EF Core
/// transaction. The outbox INSERT runs on the same NpgsqlConnection +
/// NpgsqlTransaction that EF Core is using inside SaveChangesAsync, so the
/// outbox row and the aggregate state change are committed (or rolled back)
/// atomically — the dual-write guarantee that the transactional outbox is
/// supposed to deliver.
/// </summary>
/// <remarks>
/// Implements <see cref="IPostgreSqlTransactionConnectionProvider"/>, which
/// itself extends <see cref="IPostgreSqlConnectionProvider"/> (Brighter's
/// PostgreSQL-specific provider contract that the outbox calls into) and
/// <see cref="IAmABoxTransactionConnectionProvider"/> (the framework-wide
/// marker passed as the <c>transactionConnectionProvider</c> argument to
/// <c>AddAsync</c>). The combined interface is the contract <c>PostgreSqlOutbox.GetConnectionProvider</c>
/// hard-checks at runtime — implementing only the two component interfaces
/// causes a runtime cast failure ("does not implement
/// IPostgreSqlTransactionConnectionProvider").
/// </remarks>
public sealed class EntityFrameworkPostgreSqlConnectionProvider :
    IPostgreSqlTransactionConnectionProvider
{
    private readonly ZetAuctionDbContext _dbContext;

    public EntityFrameworkPostgreSqlConnectionProvider(ZetAuctionDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public NpgsqlConnection GetConnection()
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            connection.Open();
        }
        return connection;
    }

    public async Task<NpgsqlConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connection = (NpgsqlConnection)_dbContext.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }
        return connection;
    }

    /// <summary>
    /// Returns the underlying <see cref="NpgsqlTransaction"/> for the active
    /// EF Core relational transaction, or <c>null</c> if none is open.
    /// </summary>
    /// <remarks>
    /// Brighter's interface declares this as non-nullable, but its own
    /// <c>PostgreSqlNpgsqlConnectionProvider</c> returns <c>null</c> when no
    /// transaction is active and the outbox guards calls to this method
    /// with <see cref="HasOpenTransaction"/>. We follow the same convention.
    /// </remarks>
    public NpgsqlTransaction GetTransaction()
    {
        var efTransaction = _dbContext.Database.CurrentTransaction;
        if (efTransaction is null)
        {
            return null!;
        }

        return (NpgsqlTransaction)efTransaction.GetDbTransaction();
    }

    public bool HasOpenTransaction => _dbContext.Database.CurrentTransaction is not null;

    /// <summary>
    /// Always <c>true</c>: EF owns the connection lifetime, so the outbox
    /// must not close or dispose it.
    /// </summary>
    public bool IsSharedConnection => true;
}
