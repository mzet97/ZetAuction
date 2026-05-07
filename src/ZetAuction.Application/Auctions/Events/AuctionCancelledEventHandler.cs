using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.Application.Auctions.Events;

public sealed class AuctionCancelledEventHandler : RequestHandlerAsync<AuctionCancelledEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuctionCancelledEventHandler> _logger;

    public AuctionCancelledEventHandler(IUnitOfWork unitOfWork, ILogger<AuctionCancelledEventHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override async Task<AuctionCancelledEvent> HandleAsync(AuctionCancelledEvent @event, CancellationToken cancellationToken = default)
    {
        var auction = await _unitOfWork.Auctions.GetByIdAsync(@event.AuctionId);

        _logger.LogInformation(
            "Processed AuctionCancelledEvent {EventId} for auction {AuctionId}: status {Status}, reason {Reason}",
            @event.EventId,
            @event.AuctionId,
            auction?.Status.ToString() ?? "Unknown",
            @event.Reason);

        return await base.HandleAsync(@event, cancellationToken);
    }
}
