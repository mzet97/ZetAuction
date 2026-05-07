using Microsoft.Extensions.Logging;
using Paramore.Brighter;
using ZetAuction.Application.Auth.Responses;
using ZetAuction.Application.Common;
using ZetAuction.Application.Services;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auth.Commands;

public class LoginCommandHandler : RequestHandlerAsync<LoginCommand>
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtService _jwtService;
    private readonly ILogger<LoginCommandHandler> _logger;

    public LoginCommandHandler(
        IUnitOfWork unitOfWork,
        IPasswordHasher passwordHasher,
        IJwtService jwtService,
        ILogger<LoginCommandHandler> logger)
    {
        _unitOfWork = unitOfWork;
        _passwordHasher = passwordHasher;
        _jwtService = jwtService;
        _logger = logger;
    }

    public override async Task<LoginCommand> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _unitOfWork.Users.GetByEmailAsync(command.Email, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("Login failed: user with email {Email} not found", command.Email);
            command.Result = BaseResult<AuthResponse>.Fail("Invalid email or password.");
            return await base.HandleAsync(command, cancellationToken);
        }

        if (!_passwordHasher.VerifyPassword(command.Password, user.PasswordHash))
        {
            _logger.LogWarning("Login failed: invalid password for user {Email}", command.Email);
            command.Result = BaseResult<AuthResponse>.Fail("Invalid email or password.");
            return await base.HandleAsync(command, cancellationToken);
        }

        var token = _jwtService.GenerateToken(user.Id, user.Email, user.Role.ToString());

        var response = new AuthResponse
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            User = user.ToViewModel()
        };

        _logger.LogInformation("User {UserId} logged in successfully with email {Email}", user.Id, command.Email);

        command.Result = BaseResult<AuthResponse>.Ok(response, "Login successful.");
        return await base.HandleAsync(command, cancellationToken);
    }
}
