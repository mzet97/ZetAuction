using FluentValidation;
using FluentValidation.Results;
using ZetAuction.Domain.Bids;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Shared.Domain;
using ZetAuction.Shared.Services;

namespace ZetAuction.Domain.Auctions;

public class Auction : SoftDeletableAggregateRoot<Guid>
{
    private static readonly AuctionValidator Validator = new();
    private readonly List<Bid> _bids = [];

    private Auction() { }

    public Auction(
        string title,
        string description,
        decimal startingPrice,
        decimal minBidIncrement,
        DateTime endDate,
        Guid createdByUserId)
    {
        Id = Guid.NewGuid();
        Title = title;
        Description = description;
        StartingPrice = startingPrice;
        MinBidIncrement = minBidIncrement;
        CurrentPrice = startingPrice;
        EndDate = endDate;
        Status = AuctionStatus.Draft;
        CreatedByUserId = createdByUserId;
        WinnerId = null;
    }

    public Auction(
        string title,
        string description,
        decimal startingPrice,
        DateTime endDate,
        Guid createdByUserId)
        : this(title, description, startingPrice, 1m, endDate, createdByUserId)
    {
    }

    public string Title { get; private set; } = default!;

    public string Description { get; private set; } = default!;

    public decimal StartingPrice { get; private set; }

    public decimal MinBidIncrement { get; private set; }

    public decimal CurrentPrice { get; private set; }

    public decimal? HighestBid => _bids.Count == 0 ? null : CurrentPrice;

    public DateTime EndDate { get; private set; }

    public AuctionStatus Status { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public Guid? WinnerId { get; private set; }

    public Guid? WinningBidId { get; private set; }

    public decimal? WinningAmount { get; private set; }

    public DateTime? FinalizedAtUtc { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. Mapped to Postgres' <c>xmin</c> system column
    /// in <c>AuctionMapping</c> so concurrent updates from multiple API instances
    /// are rejected by the database with <c>DbUpdateConcurrencyException</c> and
    /// retried by <c>PlaceBidCommandHandler</c>.
    /// </summary>
    public uint RowVersion { get; private set; }

    public IReadOnlyList<Bid> Bids => _bids.AsReadOnly();

    public ValidationResult Validate() => Validator.Validate(this);

    public bool IsValid() => Validate().IsValid;

    public void Activate()
    {
        if (Status != AuctionStatus.Draft)
            throw new DomainException($"Cannot activate auction in status '{Status}'. Only drafts can be activated.");

        Status = AuctionStatus.Active;
    }

    public void Cancel(string? reason = null)
    {
        if (Status is not (AuctionStatus.Draft or AuctionStatus.Active))
            throw new DomainException($"Cannot cancel auction in status '{Status}'.");

        Status = AuctionStatus.Cancelled;

        AddDomainEvent(new AuctionCancelledEvent
        {
            AuctionId = Id,
            CancelledAtUtc = DateTime.UtcNow,
            Reason = reason
        });
    }

    public Bid PlaceBid(Guid userId, decimal amount, IDateTimeProvider dateTimeProvider)
    {
        ArgumentNullException.ThrowIfNull(dateTimeProvider);

        EnsureAuctionIsActive();

        if (dateTimeProvider.UtcNow >= EndDate)
            throw new InvalidBidException($"Auction '{Id}' has ended and no longer accepts bids.");

        if (userId == CreatedByUserId)
            throw new InvalidBidException("Auction creator cannot bid on their own auction.");

        var requiredAmount = _bids.Count == 0
            ? StartingPrice
            : CurrentPrice + MinBidIncrement;

        if (amount < requiredAmount)
            throw new InsufficientBidAmountException(amount, requiredAmount);

        var previousAmount = CurrentPrice;

        var bid = new Bid(Id, userId, amount, dateTimeProvider.UtcNow);
        _bids.Add(bid);

        CurrentPrice = amount;

        AddDomainEvent(new BidPlacedEvent
        {
            AuctionId = Id,
            UserId = userId,
            Amount = amount,
            PreviousAmount = previousAmount,
            PlacedAtUtc = bid.PlacedAtUtc
        });

        return bid;
    }

    public void Close()
    {
        Close(DateTime.UtcNow);
    }

    public void Close(DateTime utcNow)
    {
        EnsureAuctionIsActive();

        Status = AuctionStatus.Finalized;
        FinalizedAtUtc = utcNow;

        var winningBid = _bids.MaxBy(b => b.Amount);

        if (winningBid is not null)
            MarkWinner(winningBid.Id, winningBid.UserId, winningBid.Amount);

        AddDomainEvent(new AuctionClosedEvent
        {
            AuctionId = Id,
            WinnerId = WinnerId,
            WinningAmount = winningBid?.Amount,
            ClosedAtUtc = utcNow
        });
    }

    public void MarkWinner(Guid? winningBidId, Guid winnerId, decimal winningAmount)
    {
        if (winnerId == Guid.Empty)
            throw new DomainException("WinnerId must be a valid non-empty GUID.");

        if (winningAmount <= 0)
            throw new DomainException("Winning amount must be greater than zero.");

        WinningBidId = winningBidId;
        WinnerId = winnerId;
        WinningAmount = winningAmount;
        CurrentPrice = winningAmount;
    }

    private void EnsureAuctionIsActive()
    {
        if (Status != AuctionStatus.Active)
            throw new AuctionClosedException(Id, $"Auction '{Id}' is not active (current status: {Status}).");
    }
}

internal sealed class AuctionValidator : AbstractValidator<Auction>
{
    public AuctionValidator()
    {
        RuleFor(a => a.Title)
            .NotEmpty().WithMessage("Title is required.");

        RuleFor(a => a.Description)
            .NotEmpty().WithMessage("Description is required.");

        RuleFor(a => a.StartingPrice)
            .GreaterThan(0).WithMessage("Starting price must be greater than zero.");

        RuleFor(a => a.MinBidIncrement)
            .GreaterThan(0).WithMessage("Minimum bid increment must be greater than zero.");

        RuleFor(a => a.EndDate)
            .GreaterThan(DateTime.UtcNow).WithMessage("End date must be in the future.");

        RuleFor(a => a.Status)
            .IsInEnum().WithMessage("A valid auction status must be specified.");

        RuleFor(a => a.CreatedByUserId)
            .NotEqual(Guid.Empty).WithMessage("CreatedByUserId is required.");
    }
}
