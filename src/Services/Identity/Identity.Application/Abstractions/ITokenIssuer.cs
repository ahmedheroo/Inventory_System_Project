using InventorySystem.Identity.Domain.Entities;

namespace InventorySystem.Identity.Application.Abstractions;

public interface ITokenIssuer
{
    IssuedToken Issue(User user, DateTimeOffset issuedAt);
}

public sealed record IssuedToken(string AccessToken, DateTimeOffset ExpiresAt);
