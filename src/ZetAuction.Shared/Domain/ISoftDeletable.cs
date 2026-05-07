namespace ZetAuction.Shared.Domain;

public interface ISoftDeletable
{
    DateTime? DeletedAtUtc { get; }

    bool IsDeleted { get; }

    void SoftDelete(DateTime utcNow);

    void Restore();
}
