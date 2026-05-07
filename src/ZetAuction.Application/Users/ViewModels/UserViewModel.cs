namespace ZetAuction.Application.Users.ViewModels;

public class UserViewModel
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Email { get; set; } = default!;
    public string Role { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
}
