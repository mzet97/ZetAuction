namespace ZetAuction.Shared.Domain;

public abstract class AuditableEntity<TId> : Entity<TId>, IAuditable where TId : notnull
{
    public DateTime CreatedAtUtc { get; protected set; }

    public DateTime UpdatedAtUtc { get; protected set; }

    public void MarkCreated(DateTime utcNow)
    {
        CreatedAtUtc = utcNow;
        UpdatedAtUtc = utcNow;
    }

    public void MarkUpdated(DateTime utcNow)
    {
        UpdatedAtUtc = utcNow;
    }
}
