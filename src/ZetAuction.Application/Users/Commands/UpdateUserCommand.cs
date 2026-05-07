using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Commands;

public class UpdateUserCommand : ResultCommand<BaseResult>
{
    public Guid UserId { get; set; }
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
}
