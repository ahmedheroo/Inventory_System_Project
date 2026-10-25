using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace InventorySystem.BuildingBlocks.Security;

/// <summary>A token and the moment it stops working.</summary>
public sealed record IssuedJwt(string AccessToken, DateTimeOffset ExpiresAt);

/// <summary>
/// Writes the token. One implementation, used by the Identity service that issues them — and by the
/// tests of the services that validate them, so a test token is the same shape as a real one.
/// </summary>
public sealed class JwtTokenFactory
{
    private readonly JwtOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenFactory(JwtOptions options)
    {
        _options = options;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public JwtOptions Options => _options;

    /// <summary>
    /// Signs the claims, dating the token from <paramref name="issuedAt"/> and expiring it after the
    /// configured lifetime. Callers pass the claims: this type knows how a token is written, not what
    /// belongs in one.
    /// </summary>
    public IssuedJwt Issue(IEnumerable<Claim> claims, DateTimeOffset issuedAt)
    {
        var expiresAt = issuedAt.Add(_options.Lifetime);

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = issuedAt.UtcDateTime,
            NotBefore = issuedAt.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = claims
                .GroupBy(claim => claim.Type)
                .ToDictionary(
                    group => group.Key,
                    group => group.Count() == 1
                        ? (object)group.First().Value
                        : group.Select(claim => claim.Value).ToArray()),
            SigningCredentials = _credentials
        };

        var token = _handler.CreateToken(descriptor);

        return new IssuedJwt(token, expiresAt);
    }

    /// <summary>
    /// Reads the claims back out without validating the signature: what a test needs, never a service.
    ///
    /// The payload is read as JSON rather than through a token handler, on purpose. A handler maps
    /// the short names onto the long XML-schema ones as it reads them, so a test that used one would
    /// be checking a claim set no service ever sees — the services read what is written ("sub",
    /// "role", "scope"), and so does this.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> ReadClaims(string token)
    {
        var parts = token.Split('.');

        if (parts.Length < 2)
        {
            throw new ArgumentException("That is not a token: it has no payload.", nameof(token));
        }

        using var document = JsonDocument.Parse(Decode(parts[1]));

        var claims = new Dictionary<string, string[]>(StringComparer.Ordinal);

        foreach (var claim in document.RootElement.EnumerateObject())
        {
            claims[claim.Name] = claim.Value.ValueKind switch
            {
                JsonValueKind.Array => claim.Value.EnumerateArray()
                    .Select(element => element.GetString() ?? element.GetRawText())
                    .ToArray(),
                JsonValueKind.String => [claim.Value.GetString() ?? string.Empty],
                _ => [claim.Value.GetRawText()]
            };
        }

        return claims;
    }

    /// <summary>Base64url, the alphabet a token is written in: no padding, and two characters swapped.</summary>
    private static string Decode(string segment)
    {
        var padded = segment.Replace('-', '+').Replace('_', '/');

        padded = (padded.Length % 4) switch
        {
            2 => padded + "==",
            3 => padded + "=",
            _ => padded
        };

        return Encoding.UTF8.GetString(Convert.FromBase64String(padded));
    }
}
