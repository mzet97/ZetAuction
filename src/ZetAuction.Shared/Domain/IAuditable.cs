namespace ZetAuction.Shared.Domain;

public interface IAuditable
{
    DateTime CreatedAtUtc { get; }

    DateTime UpdatedAtUtc { get; }
}
