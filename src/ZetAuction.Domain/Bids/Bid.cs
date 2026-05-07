using FluentValidation;
using FluentValidation.Results;
using ZetAuction.Shared.Domain;

namespace ZetAuction.Domain.Bids;

public class Bid : AuditableEntity<Guid>
{
    private static readonly BidValidator Validator = new();

    private Bid() { }

    public Bid(Guid auctionId, Guid userId, decimal amount, DateTime placedAtUtc)
    {
        Id = Guid.NewGuid();
        AuctionId = auctionId;
        UserId = userId;
        Amount = amount;
        PlacedAtUtc = placedAtUtc;
    }

    public Guid AuctionId { get; private set; }

    public Guid UserId { get; private set; }

    public decimal Amount { get; private set; }

    public DateTime PlacedAtUtc { get; private set; }

    public ValidationResult Validate() => Validator.Validate(this);

    public bool IsValid() => Validate().IsValid;
}

internal sealed class BidValidator : AbstractValidator<Bid>
{
    public BidValidator()
    {
        RuleFor(b => b.AuctionId)
            .NotEqual(Guid.Empty).WithMessage("AuctionId is required.");

        RuleFor(b => b.UserId)
            .NotEqual(Guid.Empty).WithMessage("UserId is required.");

        RuleFor(b => b.Amount)
            .GreaterThan(0).WithMessage("Bid amount must be greater than zero.");
    }
}
