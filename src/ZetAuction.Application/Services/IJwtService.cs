namespace ZetAuction.Application.Services;

public interface IJwtService
{
    string GenerateToken(Guid userId, string email, string role);

    System.Security.Claims.ClaimsPrincipal? ValidateToken(string token);
}
