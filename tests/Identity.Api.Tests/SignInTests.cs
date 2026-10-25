using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Domain.Entities;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>
/// Signing in: who is let in, who is not, and exactly what the token they get back says. The last of
/// those is the contract the rest of the system reads, so it is asserted claim by claim.
/// </summary>
public class SignInTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Signing_in_answers_with_a_token_and_who_it_is_for()
    {
        using var factory = new IdentityApiFactory();

        using var response = await factory.LoginAsync("clerk", IdentityApiFactory.UserPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SignInBody>(cancellationToken: Ct);

        Assert.NotNull(body);
        Assert.NotEmpty(body!.AccessToken);
        Assert.Equal("clerk", body.User.Username);
        Assert.Equal("clerk", body.User.Role);
        Assert.Equal("Dock clerk", body.User.DisplayName);
        Assert.True(body.User.IsActive);
        Assert.True(body.ExpiresAt > DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_username_is_read_the_way_it_is_stored()
    {
        using var factory = new IdentityApiFactory();

        // Nobody types their sign-in name in lower case.
        var body = await factory.SignInAsync("  CLERK  ", IdentityApiFactory.UserPassword);

        Assert.Equal("clerk", body.User.Username);
    }

    [Fact]
    public async Task A_wrong_password_and_an_account_that_is_not_there_get_the_same_answer()
    {
        using var factory = new IdentityApiFactory();

        using var wrongPassword = await factory.LoginAsync("clerk", "not-the-right-password");
        using var noSuchAccount = await factory.LoginAsync("night-shift", "not-the-right-password");

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, noSuchAccount.StatusCode);

        // Identical, word for word — the trace id excepted, which is per request: an answer that
        // differed would tell an attacker which usernames exist, and the whole point of the decoy
        // hash is that it takes the same work to say either.
        var refused = await Problems.DetailAsync(wrongPassword);
        var unknown = await Problems.DetailAsync(noSuchAccount);

        Assert.Equal(refused, unknown);
        Assert.Equal("Invalid username or password.", refused);
        Assert.True(wrongPassword.Headers.WwwAuthenticate.Any());

        // And nothing about the attempt is echoed back.
        Assert.DoesNotContain("clerk", refused, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_disabled_account_cannot_sign_in_and_is_told_why()
    {
        using var factory = new IdentityApiFactory();

        var id = await factory.IdOfAsync("viewer");
        var manager = await factory.TokenForAsync("manager");

        using (var client = factory.ClientWith(manager))
        {
            using var disabled = await client.PostAsync($"/api/identity/users/{id}/disable", null, Ct);

            Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        }

        // The password is still the right one; the account is not.
        using var refused = await factory.LoginAsync("viewer", IdentityApiFactory.UserPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);

        var detail = await refused.Content.ReadAsStringAsync(Ct);

        Assert.Contains("disabled", detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("viewer", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_token_carries_the_role_and_the_scopes_the_services_read()
    {
        using var factory = new IdentityApiFactory();

        var signedIn = await factory.SignInAsync("clerk", IdentityApiFactory.UserPassword);
        var claims = JwtTokenFactory.ReadClaims(signedIn.AccessToken);

        Assert.Equal(IdentityApiFactory.Tokens.Issuer, claims["iss"].Single());
        Assert.Equal(IdentityApiFactory.Tokens.Audience, claims["aud"].Single());
        Assert.Equal("clerk", claims[JwtClaimNames.Name].Single());
        Assert.Equal("Dock clerk", claims[JwtClaimNames.DisplayName].Single());
        Assert.Equal(
            signedIn.User.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            claims[JwtClaimNames.Subject].Single());
        Assert.Equal(JwtRoles.Clerk, claims[JwtClaimNames.Role].Single());

        // A clerk may read and write stock, which is what the Inventory service checks.
        Assert.Equal(
            [JwtScopes.StockRead, JwtScopes.StockWrite],
            claims[JwtClaimNames.Scope].OrderBy(scope => scope, StringComparer.Ordinal));

        // Dated, and dated for the configured lifetime rather than for ever.
        var issuedAt = long.Parse(claims["iat"].Single(), System.Globalization.CultureInfo.InvariantCulture);
        var expiresAt = long.Parse(claims["exp"].Single(), System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(TimeSpan.FromMinutes(60).TotalSeconds, expiresAt - issuedAt);
        Assert.Equal(claims["iat"].Single(), claims["nbf"].Single());
    }

    [Fact]
    public async Task A_viewer_is_given_reading_and_nothing_else()
    {
        using var factory = new IdentityApiFactory();

        var viewer = await factory.TokenForAsync("viewer");
        var claims = JwtTokenFactory.ReadClaims(viewer);

        Assert.Equal(JwtRoles.Viewer, claims[JwtClaimNames.Role].Single());
        Assert.Equal([JwtScopes.StockRead], claims[JwtClaimNames.Scope]);
    }

    [Fact]
    public async Task A_manager_is_given_the_same_scopes_as_a_clerk()
    {
        using var factory = new IdentityApiFactory();

        var manager = JwtTokenFactory.ReadClaims(await factory.TokenForAsync("manager"));

        Assert.Equal(JwtRoles.Manager, manager[JwtClaimNames.Role].Single());
        Assert.Equal([JwtScopes.StockRead, JwtScopes.StockWrite], manager[JwtClaimNames.Scope]);
    }

    [Fact]
    public async Task The_console_presents_itself_as_a_service_and_gets_nothing_it_does_not_use()
    {
        using var factory = new IdentityApiFactory();

        var token = await factory.ServiceTokenAsync();
        var claims = JwtTokenFactory.ReadClaims(token);

        Assert.Equal(JwtRoles.Service, claims[JwtClaimNames.Role].Single());

        // Least privilege: the relay exists to follow the live stock view. It is not handed the
        // scope that moves stock, because it never moves any.
        Assert.Equal([JwtScopes.StockRead], claims[JwtClaimNames.Scope]);
        Assert.DoesNotContain(JwtScopes.StockWrite, claims[JwtClaimNames.Scope]);
    }
}
