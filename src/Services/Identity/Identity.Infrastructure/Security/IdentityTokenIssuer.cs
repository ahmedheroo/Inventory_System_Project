using System.Globalization;
using System.Security.Claims;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Application.Abstractions;
using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Exceptions;

namespace InventorySystem.Identity.Infrastructure.Security;

 
public sealed class IdentityTokenIssuer : ITokenIssuer
{
    private readonly JwtTokenFactory _factory;

    public IdentityTokenIssuer(JwtTokenFactory factory)
    {
        _factory = factory;
    }

    public IssuedToken Issue(User user, DateTimeOffset issuedAt)
    {
        var claims = new List<Claim>
        {
            new(JwtClaimNames.Subject, user.Id.ToString(CultureInfo.InvariantCulture)),
            new(JwtClaimNames.Name, user.Username),
            new(JwtClaimNames.DisplayName, user.DisplayName),
            new(JwtClaimNames.Role, user.Role.WireName())
        };

        claims.AddRange(ScopesFor(user.Role).Select(scope => new Claim(JwtClaimNames.Scope, scope)));

        var issued = _factory.Issue(claims, issuedAt);

        return new IssuedToken(issued.AccessToken, issued.ExpiresAt);
    }

    public static IReadOnlyList<string> ScopesFor(UserRole role) => role switch
    {
        UserRole.Viewer => [JwtScopes.StockRead],
        UserRole.Clerk or UserRole.Manager => [JwtScopes.StockRead, JwtScopes.StockWrite],
        UserRole.Service => [JwtScopes.StockRead],
        _ => throw new DomainException($"'{role}' is not a role this system knows.")
    };
}
