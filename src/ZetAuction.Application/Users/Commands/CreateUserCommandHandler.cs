using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Domain.Exceptions;
using ZetAuction.Domain.Repositories;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Responses;
using ZetAuction.Shared.Services;

namespace ZetAuction.Application.Users.Commands;

public class CreateUserCommandHandler : RequestHandlerAsync<CreateUserCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IDateTimeProvider _dateTimeProvider;
    private readonly ILogger<CreateUserCommandHandler> _logger;

    public CreateUserCommandHandler(IUnitOfWork unitOfWork, IDateTimeProvider dateTimeProvider, ILogger<CreateUserCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _dateTimeProvider = dateTimeProvider;
        _logger = logger;
    }

    public override async Task<CreateUserCommand> HandleAsync(CreateUserCommand command, CancellationToken cancellationToken = default)
    {
        if (await _unitOfWork.Users.ExistsByEmailAsync(command.Email, cancellationToken))
        {
            _logger.LogWarning("User creation failed: email {Email} already exists", command.Email);
            command.Result = BaseResult<Guid>.Fail("A user with this email already exists.");
            return await base.HandleAsync(command, cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(command.Password))
        {
            _logger.LogError("User creation failed: password is required for email {Email}", command.Email);
            command.Result = BaseResult<Guid>.Fail("Invalid user data.");
            return await base.HandleAsync(command, cancellationToken);
        }

        var passwordHash = BCrypt.Net.BCrypt.HashPassword(command.Password);

        var user = new User(command.Name, command.Email, passwordHash, command.Role);
        user.MarkCreated(_dateTimeProvider.UtcNow);

        if (!user.IsValid())
        {
            _logger.LogError("User creation failed: invalid data for email {Email}", command.Email);
            command.Result = BaseResult<Guid>.Fail("Invalid user data.");
            return await base.HandleAsync(command, cancellationToken);
        }

        await _unitOfWork.Users.AddAsync(user);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("User {UserId} created successfully with email {Email}", user.Id, command.Email);

        command.Result = BaseResult<Guid>.Ok(user.Id, "User created successfully.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
