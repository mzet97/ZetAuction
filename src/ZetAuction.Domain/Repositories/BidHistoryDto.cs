namespace ZetAuction.Domain.Repositories;

public sealed record BidHistoryDto(
    Guid Id,
    Guid AuctionId,
    Guid UserId,
    decimal Amount,
    DateTime PlacedAtUtc,
    string UserName);
