using ZetAuction.Application.Common.Messaging;
using ZetAuction.Domain.Users;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Commands;

public class CreateUserCommand : ResultCommand<BaseResult<Guid>>
{
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
    public Role Role { get; set; }
}
