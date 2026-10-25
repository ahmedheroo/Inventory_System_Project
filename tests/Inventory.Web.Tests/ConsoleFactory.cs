using System.Net;
using System.Text.RegularExpressions;
using Inventory.Web.Api;
using Inventory.Web.Security;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace InventorySystem.Web.Tests;

/// <summary>
/// Boots the console the way a browser reaches it — the same <c>Program</c>, the same pipeline — with
/// the five service clients replaced by fakes.
///
/// The controllers are tested directly elsewhere; this is for the things that live in the pipeline and
/// cannot be seen from an action: the wall, the session cookie, what a signed-out fetch is answered
/// with, and whether the console's own hub lets a page in.
/// </summary>
public class ConsoleFactory : WebApplicationFactory<Program>
{
    public FakeCatalogApi Catalog { get; } = new();

    public FakeInventoryApi Inventory { get; } = new();

    public FakeAlertsApi Alerts { get; } = new();

    public FakeReportingApi Reporting { get; } = new();

    public FakeIdentityApi Identity { get; } = new();

    /// <summary>A console with the three staff accounts and one product to look at.</summary>
    public static ConsoleFactory WithAStockedConsole()
    {
        var factory = new ConsoleFactory();

        factory.Identity
            .With("manager", "manager-password-2026", "Office manager", ConsoleRoles.Manager)
            .With("clerk", "clerk-password-2026", "Dock clerk", ConsoleRoles.Clerk)
            .With("viewer", "viewer-password-2026", "Floor viewer", ConsoleRoles.Viewer);

        factory.Catalog.Products.Add(TestData.Product(1, "SKU-1", "Wireless Mouse", minimumStockLevel: 10));
        factory.Catalog.Warehouses.Add(TestData.Warehouse(1, "WH-CAI", "Cairo Main"));
        factory.Inventory.Stock.Add(new Inventory.Web.Api.Contracts.InventoryDto(1, 1, 1, 4, TestData.When));

        return factory;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<ICatalogApi>();
            services.AddSingleton<ICatalogApi>(Catalog);

            services.RemoveAll<IInventoryApi>();
            services.AddSingleton<IInventoryApi>(Inventory);

            services.RemoveAll<IAlertsApi>();
            services.AddSingleton<IAlertsApi>(Alerts);

            services.RemoveAll<IReportingApi>();
            services.AddSingleton<IReportingApi>(Reporting);

            services.RemoveAll<IIdentityApi>();
            services.AddSingleton<IIdentityApi>(Identity);

            // The relay is a background service that would spend the test run trying to reach an
            // Inventory service that is not there. What it does once it *is* there is tested through
            // the broadcaster, which stays registered.
            services.RemoveAll<IHostedService>();
        });
    }

    /// <summary>Signs in and answers with the session cookie the console set, as <c>name=value</c>.</summary>
    public async Task<string> SignInAsync(HttpClient client, string username, string password)
    {
        var token = await AntiforgeryTokenAsync(client);
        var response = await PostAsync(client, "/sign-in", token, new Dictionary<string, string?>
        {
            ["Username"] = username,
            ["Password"] = password
        });

        Assert.True(
            response.StatusCode is HttpStatusCode.Found or HttpStatusCode.OK,
            $"Signing in as {username} answered {(int)response.StatusCode}.");

        return SessionCookieOf(response) ?? string.Empty;
    }

    /// <summary>The antiforgery token the sign-in form carries, as a browser would submit it.</summary>
    public static async Task<string> AntiforgeryTokenAsync(HttpClient client, string path = "/sign-in")
    {
        var html = await client.GetStringAsync(path);
        var match = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");

        Assert.True(match.Success, $"The form at {path} should carry an antiforgery token.");

        return match.Groups[1].Value;
    }

    public static Task<HttpResponseMessage> PostAsync(
        HttpClient client,
        string path,
        string antiforgeryToken,
        Dictionary<string, string?> fields)
    {
        var body = fields
            .Where(field => field.Value is not null)
            .Select(field => new KeyValuePair<string, string>(field.Key, field.Value!))
            .ToList();

        body.Add(new KeyValuePair<string, string>("__RequestVerificationToken", antiforgeryToken));

        return client.PostAsync(path, new FormUrlEncodedContent(body));
    }

    /// <summary>The session cookie a response set, if it set one.</summary>
    public static string? SessionCookieOf(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out var cookies))
        {
            return null;
        }

        return cookies
            .FirstOrDefault(cookie => cookie.StartsWith(ConsoleSession.CookieName, StringComparison.Ordinal))
            ?.Split(';')[0];
    }
}
