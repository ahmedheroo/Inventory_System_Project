using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>
/// The directory: who may see it, who may change it, and what a refusal looks like. Who has access to
/// the system is not something a clerk needs to know, which is what the role wall is for.
/// </summary>
public class AccountDirectoryTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Only_a_manager_may_read_the_directory()
    {
        using var factory = new IdentityApiFactory();

        using var anonymous = factory.ClientWith(token: null);
        using var clerk = factory.ClientWith(await factory.TokenForAsync("clerk"));
        using var viewer = factory.ClientWith(await factory.TokenForAsync("viewer"));
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/identity/users", Ct)).StatusCode);

        // Signed in, and still not allowed: 403 says "I know who you are, and this is not for you".
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.GetAsync("/api/identity/users", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.GetAsync("/api/identity/users", Ct)).StatusCode);

        using var allowed = await manager.GetAsync("/api/identity/users", Ct);
        var accounts = await allowed.Content.ReadFromJsonAsync<List<UserBody>>(Ct);

        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        Assert.NotNull(accounts);

        // The four development accounts, each with the role it was seeded with.
        Assert.Equal(
            ["manager", "clerk", "viewer", "console"],
            accounts!.Select(account => account.Username));
        Assert.Equal(
            [JwtRoles.Manager, JwtRoles.Clerk, JwtRoles.Viewer, JwtRoles.Service],
            accounts.Select(account => account.Role));

        // No credential is anywhere near this answer.
        var body = await allowed.Content.ReadAsStringAsync(Ct);

        Assert.DoesNotContain("pbkdf2", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(IdentityApiFactory.UserPassword, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_service_accounts_own_token_cannot_read_the_directory()
    {
        using var factory = new IdentityApiFactory();

        // The relay's token opens the live stock view and nothing else.
        using var relay = factory.ClientWith(await factory.ServiceTokenAsync());

        Assert.Equal(HttpStatusCode.Forbidden, (await relay.GetAsync("/api/identity/users", Ct)).StatusCode);
    }

    [Fact]
    public async Task Me_says_who_the_token_belongs_to_and_refuses_an_anonymous_caller()
    {
        using var factory = new IdentityApiFactory();

        using var anonymous = factory.ClientWith(token: null);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/identity/me", Ct)).StatusCode);

        using var clerk = factory.ClientWith(await factory.TokenForAsync("clerk"));
        var me = await clerk.GetFromJsonAsync<UserBody>("/api/identity/me", Ct);

        Assert.NotNull(me);
        Assert.Equal("clerk", me!.Username);
        Assert.Equal(JwtRoles.Clerk, me.Role);
        Assert.True(me.LastSignedInAt is not null);        // signing in is what just happened
    }

    [Fact]
    public async Task A_manager_can_create_an_account_and_that_account_can_sign_in()
    {
        using var factory = new IdentityApiFactory();
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var created = await manager.PostAsJsonAsync(
            "/api/identity/users",
            new
            {
                username = "shipping.clerk",
                displayName = "Shipping clerk",
                role = "clerk",
                password = "shipping-clerk-2026"
            },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var account = await created.Content.ReadFromJsonAsync<UserBody>(Ct);

        Assert.NotNull(account);
        Assert.Equal("shipping.clerk", account!.Username);
        Assert.Equal("Shipping clerk", account.DisplayName);
        Assert.Equal(JwtRoles.Clerk, account.Role);
        Assert.True(account.IsActive);
        Assert.Null(account.LastSignedInAt);

        // And the account it created is an account, not a row: it can sign in.
        using var signedIn = await factory.LoginAsync("shipping.clerk", "shipping-clerk-2026");

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_new_clerk_cannot_reach_the_directory_either()
    {
        using var factory = new IdentityApiFactory();
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var created = await manager.PostAsJsonAsync(
            "/api/identity/users",
            new { username = "night.clerk", displayName = "Night clerk", role = "clerk", password = "night-clerk-2026" },
            Ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        using var nightClerk = factory.ClientWith(await factory.TokenForAsync("night.clerk", "night-clerk-2026"));

        Assert.Equal(HttpStatusCode.Forbidden, (await nightClerk.GetAsync("/api/identity/users", Ct)).StatusCode);
    }

    [Theory]
    [InlineData("dock clerk", "clerk", "dock-clerk-2026", "username")]
    [InlineData("dock@clerk", "clerk", "dock-clerk-2026", "username")]
    [InlineData("new.clerk", "supervisor", "dock-clerk-2026", "role")]
    [InlineData("new.clerk", "clerk", "short", "12 characters")]
    [InlineData("new.clerk", "clerk", "            ", "12 characters")]
    [InlineData("new.clerk", "clerk", "dock-clerk-2026", "display name")]
    public async Task An_account_that_breaks_a_rule_is_refused_with_the_rule(
        string username,
        string role,
        string password,
        string expectedFragment)
    {
        using var factory = new IdentityApiFactory();
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        var displayName = expectedFragment == "display name" ? "   " : "New clerk";

        using var response = await manager.PostAsJsonAsync(
            "/api/identity/users",
            new { username, displayName, role, password },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await response.Content.ReadAsStringAsync(Ct);

        Assert.Contains(expectedFragment, problem, StringComparison.OrdinalIgnoreCase);

        // Nothing was stored on the way to the refusal.
        using var after = await manager.GetAsync("/api/identity/users", Ct);
        var accounts = await after.Content.ReadFromJsonAsync<List<UserBody>>(Ct);

        Assert.DoesNotContain(accounts!, account => account.Username == username.Trim().ToLowerInvariant());
    }

    [Fact]
    public async Task A_username_that_already_has_an_account_is_refused()
    {
        using var factory = new IdentityApiFactory();
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var response = await manager.PostAsJsonAsync(
            "/api/identity/users",
            new { username = "CLERK", displayName = "The other clerk", role = "clerk", password = "another-clerk-2026" },
            Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("already taken", await response.Content.ReadAsStringAsync(Ct), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_account_that_is_not_there_is_a_404()
    {
        using var factory = new IdentityApiFactory();
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var missing = await manager.GetAsync("/api/identity/users/4242", Ct);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task An_account_can_be_disabled_and_enabled_again()
    {
        using var factory = new IdentityApiFactory();
        var id = await factory.IdOfAsync("clerk");
        using var manager = factory.ClientWith(await factory.TokenForAsync("manager"));

        using var disabled = await manager.PostAsync($"/api/identity/users/{id}/disable", null, Ct);
        var afterDisable = await disabled.Content.ReadFromJsonAsync<UserBody>(Ct);

        Assert.Equal(HttpStatusCode.OK, disabled.StatusCode);
        Assert.False(afterDisable!.IsActive);

        using var enabled = await manager.PostAsync($"/api/identity/users/{id}/enable", null, Ct);
        var afterEnable = await enabled.Content.ReadFromJsonAsync<UserBody>(Ct);

        Assert.True(afterEnable!.IsActive);

        // Restored means usable: the same password works again.
        using var signedIn = await factory.LoginAsync("clerk", IdentityApiFactory.UserPassword);

        Assert.Equal(HttpStatusCode.OK, signedIn.StatusCode);
    }

    [Fact]
    public async Task A_disabled_account_cannot_change_its_own_state()
    {
        using var factory = new IdentityApiFactory();
        var id = await factory.IdOfAsync("clerk");
        var clerkToken = await factory.TokenForAsync("clerk");

        using var clerk = factory.ClientWith(clerkToken);

        // Being signed in is not enough to hold the door open.
        Assert.Equal(HttpStatusCode.Forbidden, (await clerk.PostAsync($"/api/identity/users/{id}/disable", null, Ct)).StatusCode);
    }

    [Fact]
    public async Task A_sign_in_is_recorded_for_the_next_reader()
    {
        using var factory = new IdentityApiFactory();

        await factory.SignInAsync("clerk", IdentityApiFactory.UserPassword);
        await factory.SignInAsync("clerk", IdentityApiFactory.UserPassword);

        var clerk = (await factory.DirectoryAsync()).Single(account => account.Username == "clerk");

        Assert.NotNull(clerk.LastSignedInAt);
        Assert.True(clerk.LastSignedInAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task The_health_endpoint_says_the_directory_is_reachable()
    {
        using var factory = new IdentityApiFactory();

        using var response = await factory.CreateClient().GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("identity", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("healthy", body, StringComparison.OrdinalIgnoreCase);
    }
}
