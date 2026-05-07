using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Auctions.Commands;

public class UpdateAuctionCommandHandler : RequestHandlerAsync<UpdateAuctionCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<UpdateAuctionCommandHandler> _logger;

    public UpdateAuctionCommandHandler(IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider, ILogger<UpdateAuctionCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<UpdateAuctionCommand> HandleAsync(UpdateAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var auction = await _unitOfWork.Auctions.GetByIdAsync(command.AuctionId);
        if (auction is null)
        {
            _logger.LogWarning("Auction update failed: auction {AuctionId} not found", command.AuctionId);
            command.Result = BaseResult.Fail("Auction not found.");
            return await base.HandleAsync(command, cancellationToken);
        }

        if (auction.Status != AuctionStatus.Draft)
        {
            _logger.LogWarning("Auction update failed: auction {AuctionId} is not in Draft status (current: {Status})", command.AuctionId, auction.Status);
            command.Result = BaseResult.Fail("Only draft auctions can be updated.");
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Auctions.UpdateAsync(auction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auction {AuctionId} updated successfully", command.AuctionId);

        command.Result = BaseResult.Ok("Auction updated successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
