namespace ZetAuction.Domain.Exceptions;

public class InsufficientBidAmountException : DomainException
{
    public InsufficientBidAmountException(decimal bidAmount, decimal requiredAmount)
        : base($"Bid amount ({bidAmount:C}) must be at least {requiredAmount:C}.")
    {
        BidAmount = bidAmount;
        CurrentPrice = requiredAmount;
    }

    public decimal BidAmount { get; }

    public decimal CurrentPrice { get; }
}
