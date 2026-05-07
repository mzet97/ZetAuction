namespace ZetAuction.Domain.Repositories;

public sealed record AuctionDetailsDto(
    Guid Id,
    string Title,
    string Description,
    decimal StartingPrice,
    decimal CurrentPrice,
    DateTime EndDate,
    string Status,
    Guid CreatedByUserId,
    Guid? WinnerId,
    int BidCount,
    decimal MinBidIncrement = 0,
    Guid? WinningBidId = null,
    decimal? WinningAmount = null,
    DateTime? FinalizedAtUtc = null);
