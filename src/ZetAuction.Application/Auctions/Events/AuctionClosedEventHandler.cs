using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Bids.Events;
using ZetAuction.Domain.Repositories;

namespace ZetAuction.Application.Auctions.Events;

public sealed class AuctionClosedEventHandler : RequestHandlerAsync<AuctionClosedEvent>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<AuctionClosedEventHandler> _logger;

    public AuctionClosedEventHandler(IUnitOfWork unitOfWork, ILogger<AuctionClosedEventHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public override async Task<AuctionClosedEvent> HandleAsync(AuctionClosedEvent @event, CancellationToken cancellationToken = default)
    {
        var auction = await _unitOfWork.Auctions.GetByIdAsync(@event.AuctionId);

        _logger.LogInformation(
            "Processed AuctionClosedEvent {EventId} for auction {AuctionId}: winner {WinnerId}, winning amount {WinningAmount}, status {Status}",
            @event.EventId,
            @event.AuctionId,
            @event.WinnerId,
            @event.WinningAmount,
            auction?.Status.ToString() ?? "Unknown");

        return await base.HandleAsync(@event, cancellationToken);
    }
}
