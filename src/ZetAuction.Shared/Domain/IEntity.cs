namespace ZetAuction.Shared.Domain;

public interface IEntity<TId> where TId : notnull
{
    TId Id { get; }
}
