using ZetAuction.Application.Services;

namespace ZetAuction.Infrastructure.Auth;

/// <summary>
/// Hash de senha com BCrypt no work factor default da biblioteca (11).
/// O assessment só pede "passwords must be hashed"; BCrypt é a opção
/// pragmática — adaptive cost, salt embutido no hash, biblioteca
/// estável e amplamente revisada.
/// </summary>
public sealed class PasswordHasher : IPasswordHasher
{
    public string HashPassword(string password) =>
        BCrypt.Net.BCrypt.HashPassword(password);

    public bool VerifyPassword(string password, string hash) =>
        BCrypt.Net.BCrypt.Verify(password, hash);
}
