using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Auctions.Commands;

public class CancelAuctionCommandHandler : RequestHandlerAsync<CancelAuctionCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<CancelAuctionCommandHandler> _logger;

    public CancelAuctionCommandHandler(
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILogger<CancelAuctionCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<CancelAuctionCommand> HandleAsync(CancelAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _unitOfWork.Auctions.GetByIdAsync(command.AuctionId);
        if (auction is null)
        {
            _logger.LogWarning("Auction cancel failed: auction {AuctionId} not found", command.AuctionId);
            command.Result = BaseResult.Fail("Auction not found.");
            return await base.HandleAsync(command, cancellationToken);
        }

        try
        {
            auction.Cancel(command.Reason);
            auction.MarkUpdated(_dateTimeProvider.UtcNow);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Auction cancel rejected for auction {AuctionId}: {Message}", command.AuctionId, ex.Message);
            command.Result = BaseResult.Fail(ex.Message);
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Auctions.UpdateAsync(auction);
        // The interceptor drains AuctionCancelledEvent into the outbox
        // table within this SaveChanges, and the dispatcher worker
        // publishes it asynchronously.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auction {AuctionId} cancelled successfully. Reason: {Reason}", command.AuctionId, command.Reason);

        command.Result = BaseResult.Ok("Auction cancelled successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
