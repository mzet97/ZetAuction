using Microsoft.Extensions.Logging;
using Paramore.Darker;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Domain.Repositories;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Queries;

public class GetAllUsersQueryHandler : QueryHandlerAsync<GetAllUsersQuery, BaseResultList<UserViewModel>>
{
    private readonly IUserReadRepository _userReadRepository;
    private readonly ILogger<GetAllUsersQueryHandler> _logger;

    public GetAllUsersQueryHandler(IUserReadRepository userReadRepository, ILogger<GetAllUsersQueryHandler> logger)
    {
        _userReadRepository = userReadRepository;
        _logger = logger;
    }

    public override async Task<BaseResultList<UserViewModel>> ExecuteAsync(GetAllUsersQuery query, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Retrieving users page {Page} with size {PageSize}", query.Page, query.PageSize);

        var result = await _userReadRepository.ListAsync(query.Page, query.PageSize, cancellationToken);

        var viewModels = result.Data!.Select(u => new UserViewModel
        {
            Id = u.Id,
            Name = u.Name,
            Email = u.Email,
            Role = u.Role,
            CreatedAtUtc = u.CreatedAtUtc
        }).ToList();
        return BaseResultList<UserViewModel>.Ok(viewModels, result.PagedResult);
    }
}
