using ZetAuction.Application.Common.Messaging;
using ZetAuction.Application.Auth.Responses;
using ZetAuction.Shared.Responses;

namespace ZetAuction.Application.Auth.Commands;

public class LoginCommand : ResultCommand<BaseResult<AuthResponse>>
{
    public string Email { get; set; } = default!;
    public string Password { get; set; } = default!;
}
