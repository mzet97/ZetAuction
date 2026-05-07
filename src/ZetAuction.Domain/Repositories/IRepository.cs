using System.Linq.Expressions;
using ZetAuction.Shared.Domain;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Domain.Repositories;

public interface IRepository<TEntity> : IDisposable
    where TEntity : class, IEntity<Guid>
{
    Task AddAsync(TEntity entity);
    Task UpdateAsync(TEntity entity);
    Task RemoveAsync(Guid id);
    Task DisableAsync(Guid id);
    Task ActiveAsync(Guid id);
    Task ActiveOrDisableAsync(Guid id, bool active);

    Task<TEntity?> GetByIdAsync(Guid id);
    Task<IEnumerable<TEntity>> GetAllAsync();
    Task<IEnumerable<TEntity>> FindAsync(Expression<Func<TEntity, bool>> predicate);

    Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        int pageSize = 10, int page = 1);

    Task<BaseResultList<TEntity>> SearchAsync(
        Expression<Func<TEntity, bool>>? predicate = null,
        Func<IQueryable<TEntity>, IOrderedQueryable<TEntity>>? orderBy = null,
        string includeProperties = "",
        int pageSize = 10, int page = 1);

    Task<int> CountAsync(Expression<Func<TEntity, bool>>? predicate = null);
    Task<bool> ExistsAsync(Expression<Func<TEntity, bool>> predicate);

    IQueryable<TEntity> GetAllQueryable();
}
