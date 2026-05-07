namespace ZetAuction.Shared.Domain;

public abstract class SoftDeletableAggregateRoot<TId> : SoftDeletableEntity<TId> where TId : notnull;
