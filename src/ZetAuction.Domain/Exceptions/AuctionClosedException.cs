namespace ZetAuction.Domain.Exceptions;

public class AuctionClosedException : DomainException
{
    public AuctionClosedException(Guid auctionId)
        : base($"Auction '{auctionId}' is already closed and no longer accepts operations.")
    {
        AuctionId = auctionId;
    }

    public AuctionClosedException(Guid auctionId, string message)
        : base(message)
    {
        AuctionId = auctionId;
    }

    public Guid AuctionId { get; }
}
