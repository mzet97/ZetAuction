using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Users.Commands;

public class UpdateUserCommandHandler : RequestHandlerAsync<UpdateUserCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<UpdateUserCommandHandler> _logger;

    public UpdateUserCommandHandler(IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider, ILogger<UpdateUserCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<UpdateUserCommand> HandleAsync(UpdateUserCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Users.GetByIdAsync(command.UserId);
        if (user is null)
        {
            _logger.LogWarning("User update failed: user {UserId} not found", command.UserId);
            command.Result = BaseResult.Fail("User not found.");
            return await base.HandleAsync(command, cancellationToken);
        }

        user.UpdateProfile(command.Name, command.Email);
        user.MarkUpdated(_dateTimeProvider.UtcNow);

        if (!user.IsValid())
        {
            _logger.LogError("User update failed: invalid data for user {UserId}", command.UserId);
            command.Result = BaseResult.Fail("Invalid user data.");
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Users.UpdateAsync(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} updated successfully", command.UserId);

        command.Result = BaseResult.Ok("User updated successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
