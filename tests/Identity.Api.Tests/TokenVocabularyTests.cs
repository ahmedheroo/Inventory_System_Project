using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Exceptions;
using InventorySystem.Identity.Infrastructure.Security;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>
/// The vocabulary both ends of the wire have to agree on, and the rules about the signing key. The
/// console is a separate project that references no service, so what it knows about roles is pinned
/// here as literals — if a name changes, this is where it breaks rather than in a sign-in at a
/// customer's desk.
/// </summary>
public class TokenVocabularyTests
{
    [Fact]
    public void The_role_names_the_console_knows_are_the_ones_identity_writes()
    {
        Assert.Equal(["viewer", "clerk", "manager", "service"], new[]
        {
            UserRole.Viewer.WireName(),
            UserRole.Clerk.WireName(),
            UserRole.Manager.WireName(),
            UserRole.Service.WireName()
        });

        Assert.Equal("viewer", JwtRoles.Viewer);
        Assert.Equal("clerk", JwtRoles.Clerk);
        Assert.Equal("manager", JwtRoles.Manager);
        Assert.Equal("service", JwtRoles.Service);
    }

    [Fact]
    public void The_claim_names_are_the_ones_a_token_is_written_with()
    {
        Assert.Equal("sub", JwtClaimNames.Subject);
        Assert.Equal("name", JwtClaimNames.Name);
        Assert.Equal("display_name", JwtClaimNames.DisplayName);
        Assert.Equal("role", JwtClaimNames.Role);
        Assert.Equal("scope", JwtClaimNames.Scope);
    }

    [Fact]
    public void The_scopes_and_the_policy_are_the_ones_the_inventory_service_checks()
    {
        // Pinned on both sides: these two strings are the whole contract between the console's token
        // and the live stock view.
        Assert.Equal("stock.read", JwtScopes.StockRead);
        Assert.Equal("stock.write", JwtScopes.StockWrite);
        Assert.Equal("stock:read", JwtPolicies.StockRead);
    }

    [Theory]
    [InlineData(UserRole.Viewer, new[] { "stock.read" })]
    [InlineData(UserRole.Clerk, new[] { "stock.read", "stock.write" })]
    [InlineData(UserRole.Manager, new[] { "stock.read", "stock.write" })]
    [InlineData(UserRole.Service, new[] { "stock.read" })]
    public void A_role_is_worth_a_stated_set_of_scopes(UserRole role, string[] expected)
    {
        Assert.Equal(expected, IdentityTokenIssuer.ScopesFor(role));
    }

    [Fact]
    public void A_role_that_does_not_exist_is_worth_nothing()
    {
        Assert.Throws<DomainException>(() => IdentityTokenIssuer.ScopesFor((UserRole)99));
    }

    [Fact]
    public void Configuration_out_of_the_box_names_the_issuer_the_audience_and_the_published_key()
    {
        var options = JwtOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal(JwtOptions.DefaultIssuer, options.Issuer);
        Assert.Equal(JwtOptions.DefaultAudience, options.Audience);
        Assert.True(options.IsDevelopmentKey);
        Assert.Equal(JwtOptions.DefaultLifetime, options.Lifetime);
    }

    [Fact]
    public void The_lifetime_and_the_identity_can_be_configured()
    {
        var options = JwtOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Issuer"] = "somewhere-else",
                ["Jwt:Audience"] = "another-api",
                ["Jwt:SigningKey"] = "a-real-key-long-enough-to-sign-tokens",
                ["Jwt:LifetimeMinutes"] = "15"
            })
            .Build());

        Assert.Equal("somewhere-else", options.Issuer);
        Assert.Equal("another-api", options.Audience);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lifetime);
        Assert.False(options.IsDevelopmentKey);
    }

    [Fact]
    public void A_key_too_short_to_be_worth_signing_with_is_refused_at_startup()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = "too-short" })
            .Build();

        var refusal = Assert.Throws<InvalidOperationException>(() => JwtOptions.FromConfiguration(configuration));

        Assert.Contains($"at least {JwtOptions.MinimumSigningKeyLength} characters", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Production_refuses_to_run_on_the_published_development_key()
    {
        var development = JwtOptions.FromConfiguration(new ConfigurationBuilder().Build());

        var refusal = Assert.Throws<InvalidOperationException>(() => development.EnsureUsableFor(isProduction: true));

        Assert.Contains("development key", refusal.Message, StringComparison.OrdinalIgnoreCase);

        // Development and test environments run on it happily, and a real key is accepted anywhere.
        development.EnsureUsableFor(isProduction: false);

        var production = JwtOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:SigningKey"] = "a-key-nobody-has-read-in-this-repository"
            })
            .Build());

        production.EnsureUsableFor(isProduction: true);
    }
}
