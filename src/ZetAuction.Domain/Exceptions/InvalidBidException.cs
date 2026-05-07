namespace ZetAuction.Domain.Exceptions;

public class InvalidBidException : DomainException
{
    public InvalidBidException(string message)
        : base(message)
    {
    }

    public InvalidBidException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
