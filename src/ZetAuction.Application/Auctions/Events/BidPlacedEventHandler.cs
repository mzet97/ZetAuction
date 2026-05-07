using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.Application.Auctions.Events;

public sealed class BidPlacedEventHandler : RequestHandlerAsync<BidPlacedEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<BidPlacedEventHandler> _logger;

    public BidPlacedEventHandler(IUnitOfWork unitOfWork, ILogger<BidPlacedEventHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override async Task<BidPlacedEvent> HandleAsync(BidPlacedEvent @event, CancellationToken cancellationToken = default)
    {
        var bidCount = await _unitOfWork.Bids.CountAsync(b => b.AuctionId == @event.AuctionId);

        _logger.LogInformation(
            "Processed BidPlacedEvent {EventId} for auction {AuctionId}: user {UserId} bid {Amount}, previous amount {PreviousAmount}, total bids {BidCount}",
            @event.EventId,
            @event.AuctionId,
            @event.UserId,
            @event.Amount,
            @event.PreviousAmount,
            bidCount);

        return await base.HandleAsync(@event, cancellationToken);
    }
}
