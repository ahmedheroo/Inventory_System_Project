using InventorySystem.Identity.Domain.Entities;

namespace InventorySystem.Identity.Application.Dtos;

public sealed record UserSummary(
    int Id,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTime CreatedAt,
    DateTime? LastSignedInAt)
{
    public static UserSummary From(User user) => new(
        user.Id,
        user.Username,
        user.DisplayName,
        user.Role.WireName(),
        user.IsActive,
        user.CreatedAt,
        user.LastSignedInAt);
}

public sealed record SignInResponse(string AccessToken, DateTime ExpiresAt, UserSummary User);

public sealed record ServiceToken(string AccessToken, DateTime ExpiresAt);

public sealed record SignInRequest(string? Username, string? Password);

public sealed record ServiceTokenRequest(string? ClientId, string? ClientSecret);

public sealed record RegisterUserRequest(string? Username, string? DisplayName, string? Role, string? Password);

public sealed record ActivationRequest(bool Active);
