using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>
/// The console's own way in. A service presents a client id and a secret and is given a token; a
/// person's username and password are not a client credential, and this is where that is enforced.
/// </summary>
public class ServiceClientTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static Task<HttpResponseMessage> TokenAsync(IdentityApiFactory factory, string clientId, string secret) =>
        factory.CreateClient().PostAsJsonAsync("/api/identity/token", new { clientId, clientSecret = secret }, Ct);

    [Fact]
    public async Task The_console_is_given_a_token_for_the_live_stock_view()
    {
        using var factory = new IdentityApiFactory();

        using var response = await TokenAsync(factory, "console", IdentityApiFactory.ServiceSecret);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ServiceTokenBody>(Ct);

        Assert.NotNull(body);
        Assert.True(body!.ExpiresAt > DateTimeOffset.UtcNow);
        Assert.Equal(JwtRoles.Service, JwtTokenFactory.ReadClaims(body.AccessToken)[JwtClaimNames.Role].Single());
    }

    [Fact]
    public async Task A_person_cannot_be_used_as_a_client()
    {
        using var factory = new IdentityApiFactory();

        // Right password, wrong kind of account: the console authenticates as itself, never as
        // whoever happens to be signed in.
        using var response = await TokenAsync(factory, "manager", IdentityApiFactory.UserPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains("Invalid client credentials.", await response.Content.ReadAsStringAsync(Ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_secret_an_unknown_client_and_a_disabled_client_are_all_refused_the_same_way()
    {
        using var factory = new IdentityApiFactory();

        using var wrongSecret = await TokenAsync(factory, "console", "not-the-console-secret");
        using var unknownClient = await TokenAsync(factory, "nowhere", IdentityApiFactory.ServiceSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, wrongSecret.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownClient.StatusCode);
        Assert.Equal(
            await Problems.DetailAsync(wrongSecret),
            await Problems.DetailAsync(unknownClient));
    }

    [Fact]
    public async Task A_disabled_client_is_refused_too()
    {
        using var factory = new IdentityApiFactory();
        var id = await factory.IdOfAsync("console");
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var disabled = await manager.PostAsync($"/api/identity/users/{id}/disable", null, Ct);

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);

        using var refused = await TokenAsync(factory, "console", IdentityApiFactory.ServiceSecret);

        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }
}
