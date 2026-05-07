namespace ZetAuction.Domain.Repositories;

public sealed record UserReadDto(
    Guid Id,
    string Name,
    string Email,
    string Role,
    DateTime CreatedAtUtc);
