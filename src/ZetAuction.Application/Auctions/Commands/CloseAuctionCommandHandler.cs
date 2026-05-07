using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Auctions.Commands;

public class CloseAuctionCommandHandler : RequestHandlerAsync<CloseAuctionCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<CloseAuctionCommandHandler> _logger;

    public CloseAuctionCommandHandler(
        IUnitOfWork unitOfWork,
        IDateTimeProvider dateTimeProvider,
        ILogger<CloseAuctionCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<CloseAuctionCommand> HandleAsync(CloseAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _unitOfWork.Auctions.GetByIdWithBidsAsync(command.AuctionId, cancellationToken);
        if (auction is null)
        {
            _logger.LogWarning("Auction close failed: auction {AuctionId} not found", command.AuctionId);
            command.Result = BaseResult.Fail("Auction not found.");
            return await base.HandleAsync(command, cancellationToken);
        }

        try
        {
            auction.Close();
            auction.MarkUpdated(_dateTimeProvider.UtcNow);
        }
        catch (DomainException ex)
        {
            _logger.LogWarning(ex, "Auction close rejected for auction {AuctionId}: {Message}", command.AuctionId, ex.Message);
            command.Result = BaseResult.Fail(ex.Message);
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Auctions.UpdateAsync(auction);
        // SaveChangesAsync triggers DomainEventsSaveChangesInterceptor, which
        // drains the AuctionClosedEvent into the outbox in the same
        // transaction. The OutboxDispatcherWorker delivers it via Brighter
        // asynchronously — handlers no longer publish directly.
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auction {AuctionId} closed successfully", command.AuctionId);

        command.Result = BaseResult.Ok("Auction closed successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
