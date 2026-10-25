using Microsoft.Extensions.Configuration;

namespace InventorySystem.BuildingBlocks.Security;

/// <summary>
/// How the services agree on a token: who issued it, who it is for, and the key that proves it was
/// not written by somebody else.
///
/// The same type is loaded from the same configuration keys on both sides of the wire — the
/// Identity service that signs, and every service that validates — so the two can only disagree if
/// their configuration does. Tokens are signed with a shared symmetric key (HS256) rather than
/// discovered from an authority: this system has exactly one issuer, and a key both sides already
/// hold is one fewer moving part at startup than a metadata round-trip.
/// </summary>
/// <param name="Issuer">The <c>iss</c> claim: the Identity service.</param>
/// <param name="Audience">The <c>aud</c> claim: the services that accept these tokens.</param>
/// <param name="SigningKey">The shared secret the signature is computed with.</param>
/// <param name="Lifetime">How long an issued token is valid for.</param>
public sealed record JwtOptions(string Issuer, string Audience, string SigningKey, TimeSpan Lifetime)
{
    /// <summary>The issuer name written into every token this system mints.</summary>
    public const string DefaultIssuer = "inventory-identity";

    /// <summary>The audience every service validates against.</summary>
    public const string DefaultAudience = "inventory-api";

    /// <summary>
    /// The key a local run uses, shipped in configuration so a fresh clone signs in without setup.
    /// It is published, so it is not a secret — <see cref="EnsureUsableFor"/> refuses it in
    /// production, where the key must come from the environment.
    /// </summary>
    public const string DevelopmentSigningKey = "inventory-development-signing-key-32-bytes-long";

    /// <summary>HMAC-SHA256 needs at least this much key material to be worth anything.</summary>
    public const int MinimumSigningKeyLength = 32;

    /// <summary>A working day: long enough not to interrupt a shift, short enough to expire.</summary>
    public static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(8);

    public static JwtOptions FromConfiguration(IConfiguration configuration)
    {
        var issuer = Or(configuration["Jwt:Issuer"], DefaultIssuer);
        var audience = Or(configuration["Jwt:Audience"], DefaultAudience);

        var signingKey = configuration["Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(signingKey))
        {
            signingKey = DevelopmentSigningKey;
        }

        if (signingKey.Length < MinimumSigningKeyLength)
        {
            throw new InvalidOperationException(
                $"Jwt:SigningKey must be at least {MinimumSigningKeyLength} characters long; it is {signingKey.Length}.");
        }

        var lifetime = DefaultLifetime;

        if (int.TryParse(configuration["Jwt:LifetimeMinutes"], out var minutes) && minutes > 0)
        {
            lifetime = TimeSpan.FromMinutes(minutes);
        }

        return new JwtOptions(issuer, audience, signingKey, lifetime);
    }

    /// <summary>Whether this is the key published in configuration for local development.</summary>
    public bool IsDevelopmentKey =>
        string.Equals(SigningKey, DevelopmentSigningKey, StringComparison.Ordinal);

    /// <summary>
    /// Refuses to run a production environment on the published development key: anybody who
    /// has read this repository could mint a token this service would accept.
    /// </summary>
    public void EnsureUsableFor(bool isProduction)
    {
        if (isProduction && IsDevelopmentKey)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is still the development key. Set Jwt__SigningKey (at least "
                + $"{MinimumSigningKeyLength} characters) before running this service in Production.");
        }
    }

    private static string Or(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
