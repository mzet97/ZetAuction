using ZetAuction.Application.Common.Messaging;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Users.Commands;

public class DeleteUserCommand : ResultCommand<BaseResult>
{
    public Guid UserId { get; set; }
}
