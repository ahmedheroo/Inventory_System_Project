using System.Net;
using System.Text.RegularExpressions;
using Inventory.Web.Api;
using Inventory.Web.Hubs;
using Inventory.Web.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The wall (Sprint 7's other half): what a visitor without a session gets, what signing in mints,
/// and what each role may open once it has one. These drive the real pipeline rather than a
/// controller, because a wall that only exists in an attribute nobody applies is no wall at all.
/// </summary>
public class AuthenticationTests
{
    private static HttpClient Browser(ConsoleFactory factory) =>
        factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    // ------------------------------------------------------------------ wall

    [Fact]
    public async Task A_visitor_without_a_session_is_sent_to_sign_in_and_told_where_they_were_going()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var response = await client.GetAsync("/inventory/history?page=2");

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);

        Assert.Equal("/sign-in", PathOf(response));
        Assert.Contains("returnUrl=%2Finventory%2Fhistory%3Fpage%3D2", QueryOf(response), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/alerts")]
    [InlineData("/products")]
    [InlineData("/products/new")]
    [InlineData("/categories")]
    [InlineData("/warehouses")]
    [InlineData("/inventory")]
    [InlineData("/inventory/history")]
    [InlineData("/inventory/receive")]
    [InlineData("/inventory/issue")]
    [InlineData("/inventory/transfer")]
    [InlineData("/inventory/adjust")]
    [InlineData("/reports/inventory")]
    [InlineData("/reports/valuation")]
    public async Task Every_screen_is_behind_the_wall(string path)
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Equal("/sign-in", PathOf(response));
    }

    [Fact]
    public async Task The_sign_in_page_is_the_one_screen_that_is_not_behind_it()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var response = await client.GetAsync("/sign-in");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Sign in to the console", html, StringComparison.Ordinal);

        // No rail, and no live client: a visitor has no sections and nothing to follow.
        Assert.DoesNotContain("class=\"rail\"", html, StringComparison.Ordinal);
        Assert.DoesNotContain("/hubs/live", html, StringComparison.Ordinal);
        Assert.DoesNotContain("lib/signalr", html, StringComparison.Ordinal);
        Assert.DoesNotContain("js/live.js", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task What_a_page_raised_is_still_readable_without_a_session()
    {
        // These two are re-executed *after* a status was raised: a 404 that could not be rendered
        // without a session would send a visitor round the wall for ever.
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var missing = await client.GetAsync("/home/status?code=404&path=/nowhere");

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task A_live_region_answers_with_a_status_rather_than_a_sign_in_page()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var response = await client.GetAsync("/live/attention");

        // A redirect here would be fetched by the live client and its sign-in page would be injected
        // into the rail. The status is what lets the script say the session ended.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Signing_in_opens_the_console_and_lands_where_they_were_going()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        var token = await ConsoleFactory.AntiforgeryTokenAsync(client);

        using var signedIn = await ConsoleFactory.PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
        {
            ["Username"] = "clerk",
            ["Password"] = "clerk-password-2026",
            ["ReturnUrl"] = "/inventory/history"
        });

        Assert.Equal(HttpStatusCode.Found, signedIn.StatusCode);
        Assert.Equal("/inventory/history", PathOf(signedIn));

        var cookie = ConsoleFactory.SessionCookieOf(signedIn);

        Assert.NotNull(cookie);
        Assert.Contains("httponly", CookieOf(signedIn), StringComparison.OrdinalIgnoreCase);

        using var page = await client.GetAsync("/inventory");
        var html = await page.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, page.StatusCode);

        // The rail, the account and the console's own hub — the session is a session.
        Assert.Contains("class=\"rail\"", html, StringComparison.Ordinal);
        Assert.Contains("Dock clerk", html, StringComparison.Ordinal);
        Assert.Contains("Clerk", html, StringComparison.Ordinal);
        Assert.Contains($"data-live-hub=\"{LiveHub.Path}\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_sign_in_stays_on_the_page_and_repeats_what_identity_said()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        var token = await ConsoleFactory.AntiforgeryTokenAsync(client);

        using var refused = await ConsoleFactory.PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
        {
            ["Username"] = "clerk",
            ["Password"] = "not-the-password"
        });

        var html = await refused.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("Invalid username or password.", html, StringComparison.Ordinal);
        Assert.Null(ConsoleFactory.SessionCookieOf(refused));

        // And nothing was opened on the way.
        using var stillOut = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Found, stillOut.StatusCode);
        Assert.Equal("/sign-in", PathOf(stillOut));
    }

    [Fact]
    public async Task A_disabled_account_is_answered_with_identitys_own_words()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        factory.Identity.FailSignInWith = new ApiException(
            HttpStatusCode.Unauthorized,
            "Identity service",
            "The Identity service answered 401 for /api/identity/login.",
            "The account 'clerk' is disabled. Ask a manager to enable it.");

        using var client = Browser(factory);
        var token = await ConsoleFactory.AntiforgeryTokenAsync(client);

        using var refused = await ConsoleFactory.PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
        {
            ["Username"] = "clerk",
            ["Password"] = "clerk-password-2026"
        });

        Assert.Contains("disabled", await refused.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_identity_service_that_is_down_is_not_a_wrong_password()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        factory.Identity.FailSignInWith = new ApiException(
            HttpStatusCode.ServiceUnavailable,
            "Identity service",
            "The Identity service could not be reached.",
            "No connection could be made because the target machine actively refused it.");

        using var client = Browser(factory);
        var token = await ConsoleFactory.AntiforgeryTokenAsync(client);

        using var response = await ConsoleFactory.PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
        {
            ["Username"] = "clerk",
            ["Password"] = "clerk-password-2026"
        });

        var html = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode == HttpStatusCode.ServiceUnavailable,
            $"expected 503, answered {(int)response.StatusCode} → {response.Headers.Location}");
        Assert.Contains("Identity service could not be reached", html, StringComparison.Ordinal);
        Assert.DoesNotContain("Invalid username", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_form_post_without_the_token_is_refused()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        using var response = await client.PostAsync(
            "/sign-in",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["Username"] = "clerk",
                ["Password"] = "clerk-password-2026"
            }));

        Assert.True(
            response.StatusCode == HttpStatusCode.BadRequest,
            $"expected 400, answered {(int)response.StatusCode} → {response.Headers.Location}");
        Assert.Null(ConsoleFactory.SessionCookieOf(response));
    }

    // ----------------------------------------------------------------- roles

    [Theory]
    [InlineData(ConsoleRoles.Viewer, "/inventory/receive", HttpStatusCode.Found)]
    [InlineData(ConsoleRoles.Viewer, "/inventory/adjust", HttpStatusCode.Found)]
    [InlineData(ConsoleRoles.Viewer, "/products/new", HttpStatusCode.Found)]
    [InlineData(ConsoleRoles.Clerk, "/inventory/receive", HttpStatusCode.OK)]
    [InlineData(ConsoleRoles.Clerk, "/products/new", HttpStatusCode.Found)]
    [InlineData(ConsoleRoles.Clerk, "/products/1/edit", HttpStatusCode.Found)]
    [InlineData(ConsoleRoles.Manager, "/products/new", HttpStatusCode.OK)]
    [InlineData(ConsoleRoles.Manager, "/products/1/edit", HttpStatusCode.OK)]
    [InlineData(ConsoleRoles.Manager, "/inventory/receive", HttpStatusCode.OK)]
    public async Task What_a_role_may_open_follows_the_role(string role, string path, HttpStatusCode expected)
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        await SignInAsync(factory, client, role);

        using var response = await client.GetAsync(path);

        Assert.Equal(expected, response.StatusCode);

        if (expected == HttpStatusCode.Found)
        {
            Assert.Equal("/sign-in/denied", PathOf(response));
        }
    }

    [Fact]
    public async Task The_rail_offers_the_work_a_role_may_actually_do()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        using var viewerClient = Browser(factory);
        await SignInAsync(factory, viewerClient, ConsoleRoles.Viewer);
        var viewerHtml = await viewerClient.GetStringAsync("/");

        // A viewer reads the dock's numbers; the dock's work is not offered, and the catalogue pages
        // they may see do not pretend to be editable.
        Assert.DoesNotContain("Operations", viewerHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("/inventory/receive", viewerHtml, StringComparison.Ordinal);

        // The catalogue *reads* stay open to a viewer: they are how stock levels are made sense of.
        Assert.Contains("Catalogue", viewerHtml, StringComparison.OrdinalIgnoreCase);

        var products = await viewerClient.GetStringAsync("/products");

        Assert.Contains("Wireless Mouse", products, StringComparison.Ordinal);
        Assert.DoesNotContain("New product", products, StringComparison.Ordinal);
        Assert.DoesNotContain("Deactivate", products, StringComparison.Ordinal);
        Assert.DoesNotContain("Open", products, StringComparison.Ordinal);

        using var clerkClient = Browser(factory);
        await SignInAsync(factory, clerkClient, ConsoleRoles.Clerk);
        var clerkHtml = await clerkClient.GetStringAsync("/");

        Assert.Contains("Operations", clerkHtml, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/inventory/receive", clerkHtml, StringComparison.Ordinal);

        // A clerk may move stock, and still may not add products.
        var clerkProducts = await clerkClient.GetStringAsync("/products");

        Assert.DoesNotContain("New product", clerkProducts, StringComparison.Ordinal);

        using var managerClient = Browser(factory);
        await SignInAsync(factory, managerClient, ConsoleRoles.Manager);
        var managerProducts = await managerClient.GetStringAsync("/products");

        Assert.Contains("New product", managerProducts, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_viewer_sees_no_acknowledge_button_and_a_clerk_does()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        factory.Alerts.Alerts.Add(TestData.Alert(1, 1, 1, currentQuantity: 4));

        using var viewerClient = Browser(factory);
        await SignInAsync(factory, viewerClient, ConsoleRoles.Viewer);
        var viewerHtml = await viewerClient.GetStringAsync("/alerts");

        Assert.Contains("Waiting", viewerHtml, StringComparison.Ordinal);

        // The word appears in the view's own filter labels; what must not appear is the control.
        Assert.DoesNotContain(">Acknowledge<", viewerHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("Acknowledge all unread", viewerHtml, StringComparison.Ordinal);

        using var clerkClient = Browser(factory);
        await SignInAsync(factory, clerkClient, ConsoleRoles.Clerk);
        var clerkHtml = await clerkClient.GetStringAsync("/alerts");

        Assert.Contains(">Acknowledge<", clerkHtml, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------- out

    [Fact]
    public async Task The_rail_posts_sign_out_to_the_route_that_exists()
    {
        // The button is a form, so the route only works if URL generation was asked for the action's
        // name — `SignOut`, with the Async suffix trimmed — rather than the method's `SignOutAsync`,
        // which fell back to the conventional route /Account/SignOutAsync and 404ed in the browser.
        // This reads the address the page itself renders and posts to *that*, the way a browser does.
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        await SignInAsync(factory, client, ConsoleRoles.Clerk);

        var html = await client.GetStringAsync("/inventory");
        var form = Regex.Match(
            html,
            "<form[^>]*action=\"([^\"]+)\"[^>]*>(?:(?!</form>).)*Sign out",
            RegexOptions.Singleline);

        Assert.True(form.Success, "The rail should carry a sign-out form.");

        var action = form.Groups[1].Value;

        Assert.Equal("/sign-out", action);

        var token = await ConsoleFactory.AntiforgeryTokenAsync(client, "/inventory");
        using var signedOut = await client.PostAsync(action, new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Found, signedOut.StatusCode);
        Assert.Equal("/sign-in", PathOf(signedOut));

        using var page = await client.GetAsync("/inventory");

        Assert.Equal(HttpStatusCode.Found, page.StatusCode);
        Assert.Equal("/sign-in", PathOf(page));
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        using var client = Browser(factory);

        await SignInAsync(factory, client, ConsoleRoles.Clerk);

        var token = await ConsoleFactory.AntiforgeryTokenAsync(client, "/inventory");
        using var signedOut = await client.PostAsync("/sign-out", new FormUrlEncodedContent(
            new Dictionary<string, string> { ["__RequestVerificationToken"] = token }));

        Assert.Equal(HttpStatusCode.Found, signedOut.StatusCode);
        Assert.Equal("/sign-in", PathOf(signedOut));

        using var page = await client.GetAsync("/inventory");

        Assert.Equal(HttpStatusCode.Found, page.StatusCode);
        Assert.Equal("/sign-in", PathOf(page));
    }

    [Fact]
    public async Task Where_to_land_after_signing_in_is_never_another_host()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        foreach (var hostile in new[] { "https://elsewhere.example.com/console", "//elsewhere.example.com" })
        {
            // A fresh visitor per attempt: the token is read from the form as a browser would, and a
            // token minted for the anonymous visitor is refused once somebody is signed in with it.
            using var client = Browser(factory);
            var token = await ConsoleFactory.AntiforgeryTokenAsync(client);

            using var response = await ConsoleFactory.PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
            {
                ["Username"] = "viewer",
                ["Password"] = "viewer-password-2026",
                ["ReturnUrl"] = hostile
            });

            Assert.Equal("/", PathOf(response));

            // Where it lands is this console and no other: a hostile return address is not sent back
            // to, and the redirect stays on the host the browser is already talking to.
            var location = response.Headers.Location!;
            var landed = location.IsAbsoluteUri ? location : new Uri(client.BaseAddress!, location);

            Assert.Equal("localhost", landed.Host);
            Assert.Equal(client.BaseAddress!.Host, landed.Host);
        }
    }

    /// <summary>
    /// The path a redirect points at. Some of the console's redirects are relative and some are
    /// absolute, and a test that insisted on one spelling would be testing the framework.
    /// </summary>
    private static string PathOf(HttpResponseMessage response)
    {
        var location = response.Headers.Location;

        Assert.NotNull(location);

        return location!.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString.Split('?')[0];
    }

    private static string QueryOf(HttpResponseMessage response)
    {
        var location = response.Headers.Location!;

        return location.IsAbsoluteUri ? location.Query : (location.OriginalString.Contains('?') ? "?" + location.OriginalString.Split('?', 2)[1] : string.Empty);
    }

    private static async Task SignInAsync(ConsoleFactory factory, HttpClient client, string role)
    {
        var (username, password) = role switch
        {
            ConsoleRoles.Manager => ("manager", "manager-password-2026"),
            ConsoleRoles.Clerk => ("clerk", "clerk-password-2026"),
            _ => ("viewer", "viewer-password-2026")
        };

        var cookie = await factory.SignInAsync(client, username, password);

        Assert.False(string.IsNullOrEmpty(cookie), $"Signing in as {username} should have set a session cookie.");
    }

    private static string CookieOf(HttpResponseMessage response) =>
        string.Join(" ", response.Headers.TryGetValues("Set-Cookie", out var values) ? values : []);
}
