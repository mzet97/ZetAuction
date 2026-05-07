namespace ZetAuction.Shared.Domain;

public abstract class SoftDeletableEntity<TId> : AuditableEntity<TId>, ISoftDeletable where TId : notnull
{
    public DateTime? DeletedAtUtc { get; protected set; }

    public bool IsDeleted { get; protected set; }

    public void SoftDelete(DateTime utcNow)
    {
        IsDeleted = true;
        DeletedAtUtc = utcNow;
    }

    public void Restore()
    {
        IsDeleted = false;
        DeletedAtUtc = null;
    }
}
