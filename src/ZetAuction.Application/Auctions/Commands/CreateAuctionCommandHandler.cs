using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Auctions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Auctions.Commands;

public class CreateAuctionCommandHandler : RequestHandlerAsync<CreateAuctionCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<CreateAuctionCommandHandler> _logger;

    public CreateAuctionCommandHandler(IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider, ILogger<CreateAuctionCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<CreateAuctionCommand> HandleAsync(CreateAuctionCommand command, CancellationToken cancellationToken = default)
    {
        var minBidIncrement = command.MinBidIncrement > 0 ? command.MinBidIncrement : 1m;

        var auction = new Auction(
            command.Name,
            command.Description,
            command.StartingBid,
            minBidIncrement,
            command.EndDateTime,
            command.CreatedByUserId);

        auction.MarkCreated(_dateTimeProvider.UtcNow);
        auction.Activate();

        if (!auction.IsValid())
        {
            _logger.LogWarning("Auction creation failed: invalid data for name '{Name}'", command.Name);
            command.Result = BaseResult<Guid>.Fail("Invalid auction data.");
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Auctions.AddAsync(auction);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Auction {AuctionId} created successfully by user {UserId}", auction.Id, command.CreatedByUserId);

        command.Result = BaseResult<Guid>.Ok(auction.Id, "Auction created successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
