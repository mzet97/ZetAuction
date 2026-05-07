using ZetAuction.Application.Users.ViewModels;

namespace ZetAuction.Application.Auth.Responses;

public class AuthResponse
{
    public string Token { get; set; } = default!;
    public DateTime ExpiresAt { get; set; }
    public UserViewModel User { get; set; } = default!;
}
