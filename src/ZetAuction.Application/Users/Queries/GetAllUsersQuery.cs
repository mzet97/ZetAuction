using Paramore.Darker;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Queries;

public class GetAllUsersQuery : IQuery<BaseResultList<UserViewModel>>
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
