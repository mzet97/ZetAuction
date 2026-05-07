using FluentValidation;
using FluentValidation.Results;
using ZetAuction.Shared.Domain;

namespace ZetAuction.Domain.Users;

public class User : SoftDeletableEntity<Guid>
{
    private static readonly UserValidator Validator = new();

    private User() { }

    public User(string name, string email, string passwordHash, Role role)
    {
        Id = Guid.NewGuid();
        Name = name;
        Email = email;
        PasswordHash = passwordHash;
        Role = role;
    }

    public string Name { get; private set; } = default!;

    public string Email { get; private set; } = default!;

    public string PasswordHash { get; private set; } = default!;

    public Role Role { get; private set; }

    public ValidationResult Validate() => Validator.Validate(this);

    public bool IsValid() => Validate().IsValid;

    public void UpdateProfile(string name, string email)
    {
        Name = name;
        Email = email;
    }

    public void ChangePassword(string newPasswordHash)
    {
        PasswordHash = newPasswordHash;
    }

    public void ChangeRole(Role newRole)
    {
        Role = newRole;
    }
}

internal sealed class UserValidator : AbstractValidator<User>
{
    public UserValidator()
    {
        RuleFor(u => u.Name)
            .NotEmpty().WithMessage("Name is required.");

        RuleFor(u => u.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("A valid email address is required.");

        RuleFor(u => u.Role)
            .IsInEnum().WithMessage("A valid role must be specified.");
    }
}
