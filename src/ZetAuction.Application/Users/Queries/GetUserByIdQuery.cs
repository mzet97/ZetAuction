using Paramore.Darker;
using ZetAuction.Application.Users.ViewModels;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Queries;

public class GetUserByIdQuery : IQuery<BaseResult<UserViewModel>>
{
    public Guid UserId { get; set; }
}
