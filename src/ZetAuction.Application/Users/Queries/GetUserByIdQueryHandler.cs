using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Queries;

public class GetUserByIdQueryHandler : QueryHandlerAsync<GetUserByIdQuery, BaseResult<UserViewModel>>
{
    private readonly IUserReadRepository _userReadRepository;
    private readonly ILogger<GetUserByIdQueryHandler> _logger;

    public GetUserByIdQueryHandler(IUserReadRepository userReadRepository, ILogger<GetUserByIdQueryHandler> logger)
    {
        _userReadRepository = userReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResult<UserViewModel>> ExecuteAsync(GetUserByIdQuery query, CancellationToken cancellationToken = default)
    {
        var user = await _userReadRepository.GetByIdAsync(query.UserId, cancellationToken);
        if (user is null)
        {
            _logger.LogWarning("User {UserId} not found", query.UserId);
            return BaseResult<UserViewModel>.Fail("User not found.");
        }

        _logger.LogInformation("User {UserId} retrieved successfully", query.UserId);

        var viewModel = new UserViewModel
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            CreatedAtUtc = user.CreatedAtUtc
        };
        return BaseResult<UserViewModel>.Ok(viewModel);
    }
}
