using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Users.Commands;

public class DeleteUserCommandHandler : RequestHandlerAsync<DeleteUserCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<DeleteUserCommandHandler> _logger;

    public DeleteUserCommandHandler(IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider, ILogger<DeleteUserCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<DeleteUserCommand> HandleAsync(DeleteUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(command.UserId);
        if (user is null)
        {
            _logger.LogWarning("User deletion failed: user {UserId} not found", command.UserId);
            command.Result = BaseResult.Fail("User not found.");
            return await base.HandleAsync(command, cancellationToken);
        }

        user.SoftDelete(_dateTimeProvider.UtcNow);

        await _unitOfWork.Users.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} soft-deleted successfully", command.UserId);

        command.Result = BaseResult.Ok("User deleted successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
