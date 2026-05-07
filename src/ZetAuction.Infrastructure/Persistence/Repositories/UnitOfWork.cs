using Microsoft.EntityFrameworkCore;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

/// <summary>
/// Unit of Work implementation coordinating transactions across repositories.
/// </summary>
public class UnitOfWork : IUnitOfWork
{
    private readonly ZetAuctionDbContext _context;
    private readonly IDateTimeProvider _dateTimeProvider;

    public UnitOfWork(ZetAuctionDbContext context, IDateTimeProvider dateTimeProvider)
    {
        _context = context;
        _dateTimeProvider = dateTimeProvider;
    }

    private IUserRepository? _userRepository;
    public IUserRepository Users =>
        _userRepository ??= new UserRepository(_context, _dateTimeProvider);

    private IAuctionRepository? _auctionRepository;
    public IAuctionRepository Auctions =>
        _auctionRepository ??= new AuctionRepository(_context, _dateTimeProvider);

    private IBidRepository? _bidRepository;
    public IBidRepository Bids =>
        _bidRepository ??= new BidRepository(_context, _dateTimeProvider);

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            throw TranslateConcurrencyException(ex);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction == null)
        {
            // Forwarding the token lets callers bound the wait when the
            // connection pool is saturated and the transaction begin
            // request would otherwise queue indefinitely.
            await _context.Database.BeginTransactionAsync(cancellationToken);
        }
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction == null)
        {
            return;
        }

        try
        {
            await _context.SaveChangesAsync(cancellationToken);
            await _context.Database.CommitTransactionAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await SafeRollbackAsync();
            throw TranslateConcurrencyException(ex);
        }
    }

    public async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        if (_context.Database.CurrentTransaction != null)
        {
            await _context.Database.RollbackTransactionAsync(cancellationToken);
        }
    }

    public Task ResetTrackingAsync()
    {
        // Drops every tracked entity, including those scheduled for insert,
        // update or delete. Required between retry attempts because a failed
        // SaveChanges leaves the rejected entity attached with its stale
        // concurrency token, which would re-raise the same conflict on the
        // next save no matter what we reload.
        _context.ChangeTracker.Clear();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _context.Dispose();
    }

    private async Task SafeRollbackAsync()
    {
        try
        {
            if (_context.Database.CurrentTransaction != null)
            {
                await _context.Database.RollbackTransactionAsync();
            }
        }
        catch
        {
            // Rollback during an already-failing path; swallow to surface
            // the original concurrency exception.
        }
    }

    private static ConcurrencyConflictException TranslateConcurrencyException(DbUpdateConcurrencyException ex)
    {
        var entry = ex.Entries.FirstOrDefault();
        var aggregateName = entry?.Entity.GetType().Name ?? "Aggregate";
        var aggregateId = entry?.Properties
            .FirstOrDefault(p => p.Metadata.IsPrimaryKey())?.CurrentValue;
        return new ConcurrencyConflictException(aggregateName, aggregateId, ex);
    }
}
