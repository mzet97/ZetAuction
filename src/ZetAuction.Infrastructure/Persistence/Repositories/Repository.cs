using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Domain;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Infrastructure.Persistence.Repositories;

/// <summary>
/// Generic EF Core repository providing common CRUD operations.
/// Specific repositories extend this class for domain-specific queries.
/// </summary>
public abstract class Repository<TEntity> : IRepository<TEntity>
    where TEntity : class, IEntity<Guid>
{
    protected readonly ZetAuctionDbContext Db;
    protected readonly DbSet<TEntity> DbSet;
    protected readonly IDateTimeProvider DateTimeProvider;

    protected Repository(ZetAuctionDbContext db, IDateTimeProvider dateTimeProvider)
    {
        Db = db ?? throw new ArgumentNullException(nameof(db));
        DateTimeProvider = dateTimeProvider ?? throw new ArgumentNullException(nameof(dateTimeProvider));
        DbSet = db.Set<TEntity>();
    }

    public virtual async Task AddAsync(TEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        if (entity is IAuditable auditable)
            auditable.GetType().GetProperty(nameof(IAuditable.CreatedAtUtc))?
                .SetValue(auditable, DateTimeProvider.UtcNow);

        await DbSet.AddAsync(entity);
    }

    public virtual async Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int pageSize = 10, int page = 1)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));

        var query = DbSet.AsNoTracking().AsQueryable();

        if (predicate != null)
            query = query.Where(predicate);

        var totalCount = await query.CountAsync();
        var paged = PagedResult.Create(page, pageSize, totalCount);

        if (orderBy != null)
            query = orderBy(query);

        var data = await query.Skip(paged.Skip()).Take(pageSize).ToListAsync();
        return new BaseResultList<TEntity>(data, paged);
    }

    public virtual async Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string includeProperties = "",
        int pageSize = 10, int page = 1)
    {
        if (pageSize <= 0) throw new ArgumentOutOfRangeException(nameof(pageSize));
        if (page <= 0) throw new ArgumentOutOfRangeException(nameof(page));

        var query = DbSet.AsNoTracking().AsQueryable();

        if (predicate != null)
            query = query.Where(predicate);

        var totalCount = await query.CountAsync();
        var paged = PagedResult.Create(page, pageSize, totalCount);

        if (orderBy != null)
            query = orderBy(query);

        foreach (var includeProperty in includeProperties
                     .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
        {
            query = query.Include(includeProperty);
        }

        var data = await query.Skip(paged.Skip()).Take(pageSize).ToListAsync();
        return new BaseResultList<TEntity>(data, paged);
    }

    public virtual async Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return await DbSet.AsNoTracking().Where(predicate).ToListAsync();
    }

    public virtual async Task<IEnumerable<TEntity>> GetAllAsync()
        => await DbSet.AsNoTracking().ToListAsync();

    public virtual async Task<TEntity?> GetByIdAsync(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", nameof(id));
        return await DbSet.AsNoTracking().FirstOrDefaultAsync(e => e.Id == id);
    }

    public virtual Task UpdateAsync(TEntity entity)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));

        if (entity is IAuditable auditable)
            auditable.GetType().GetProperty(nameof(IAuditable.UpdatedAtUtc))?
                .SetValue(auditable, DateTimeProvider.UtcNow);

        DbSet.Update(entity);
        return Task.CompletedTask;
    }

    public virtual async Task RemoveAsync(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", nameof(id));

        var entity = await DbSet.FindAsync(id);

        if (entity == null)
            throw new InvalidOperationException("Entity not found for deletion.");

        DbSet.Remove(entity);
    }

    public virtual async Task DisableAsync(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", nameof(id));

        var entity = await DbSet.FindAsync(id);
        if (entity == null) return;

        if (entity is ISoftDeletable soft)
        {
            soft.GetType().GetMethod(nameof(ISoftDeletable.SoftDelete))?
                .Invoke(soft, new object[] { DateTimeProvider.UtcNow });
            DbSet.Update(entity);
            return;
        }

        DbSet.Remove(entity);
    }

    public virtual async Task ActiveAsync(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("ID cannot be empty.", nameof(id));

        var entity = await DbSet.FindAsync(id);
        if (entity == null) return;

        if (entity is ISoftDeletable soft)
        {
            soft.GetType().GetMethod(nameof(ISoftDeletable.Restore))?
                .Invoke(soft, null);
            DbSet.Update(entity);
        }
    }

    public Task ActiveOrDisableAsync(Guid id, bool active)
        => active ? ActiveAsync(id) : DisableAsync(id);

    public async Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null)
        => predicate == null ? await DbSet.CountAsync() : await DbSet.CountAsync(predicate);

    public virtual async Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));
        return await DbSet.AsNoTracking().AnyAsync(predicate);
    }

    public virtual IQueryable<TEntity> GetAllQueryable()
        => DbSet.AsNoTracking().AsQueryable();

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
            Db?.Dispose();
    }
}
