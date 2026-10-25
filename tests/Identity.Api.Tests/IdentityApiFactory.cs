using System.Net.Http.Headers;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Identity.Api.Tests;

/// <summary>
/// Boots the real Identity API on a throwaway SQLite database, seeds the development accounts (so
/// there is somebody to sign in as) and pins the tokens it accepts — the same shape the Inventory
/// service validates, set here rather than left to the published defaults so a test can never pass
/// because the service happened to agree with itself.
/// </summary>
public class IdentityApiFactory : WebApplicationFactory<Program>
{
    /// <summary>The password every seeded development person shares, in tests.</summary>
    public const string UserPassword = "identity-test-password";

    /// <summary>The console's client secret, in tests.</summary>
    public const string ServiceSecret = "identity-test-console-secret";

    public static readonly JwtOptions Tokens = new(
        "inventory-identity-tests",
        "inventory-api-tests",
        "identity-tests-signing-key-long-enough",
        TimeSpan.FromMinutes(60));

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"identity-tests-{Guid.NewGuid():N}.db");

    public string DatabasePath => _databasePath;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, configuration) =>
        {
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Database:Provider"] = "Sqlite",
                ["ConnectionStrings:IdentityDb"] = $"Data Source={_databasePath}",
                ["Identity:Seed:Enabled"] = "true",
                ["Identity:Seed:UserPassword"] = UserPassword,
                ["Identity:Seed:ServiceSecret"] = ServiceSecret,
                ["Jwt:Issuer"] = Tokens.Issuer,
                ["Jwt:Audience"] = Tokens.Audience,
                ["Jwt:SigningKey"] = Tokens.SigningKey,
                ["Jwt:LifetimeMinutes"] = Tokens.Lifetime.TotalMinutes.ToString(
                    System.Globalization.CultureInfo.InvariantCulture)
            });
        });
    }

    /// <summary>A client that presents this token, or an anonymous one when there is none.</summary>
    public HttpClient ClientWith(string? token)
    {
        var client = CreateClient();

        if (token is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return client;
    }

    /// <summary>The console, as far as this service is concerned: a client with its own credentials.</summary>
    public async Task<string> ServiceTokenAsync()
    {
        using var response = await CreateClient().PostAsJsonAsync(
            "/api/identity/token",
            new { clientId = "console", clientSecret = ServiceSecret });

        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<ServiceTokenBody>();

        return body!.AccessToken;
    }

    public async Task<HttpResponseMessage> LoginAsync(string username, string password) =>
        await CreateClient().PostAsJsonAsync("/api/identity/login", new { username, password });

    /// <summary>Signs in, or fails the test with the response body when the sign-in was refused.</summary>
    public async Task<SignInBody> SignInAsync(string username, string password)
    {
        using var response = await LoginAsync(username, password);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.IsSuccessStatusCode, $"Signing in as {username} answered {(int)response.StatusCode}: {body}");

        return System.Text.Json.JsonSerializer.Deserialize<SignInBody>(body, Json)!;
    }

    public async Task<string> TokenForAsync(string username, string password = UserPassword) =>
        (await SignInAsync(username, password)).AccessToken;

    /// <summary>The account directory, as a manager sees it.</summary>
    public async Task<IReadOnlyList<UserBody>> DirectoryAsync()
    {
        var manager = await TokenForAsync("manager");

        using var client = ClientWith(manager);

        return await client.GetFromJsonAsync<List<UserBody>>("/api/identity/users") ?? [];
    }

    public async Task<int> IdOfAsync(string username) =>
        (await DirectoryAsync()).Single(user => user.Username == username).Id;

    private static readonly System.Text.Json.JsonSerializerOptions Json =
        new(System.Text.Json.JsonSerializerDefaults.Web);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (!disposing)
        {
            return;
        }

        try
        {
            if (File.Exists(_databasePath))
            {
                File.Delete(_databasePath);
            }
        }
        catch (IOException)
        {
            // Still held by a pooled connection; a leftover temp file is harmless.
        }
    }
}

/// <summary>The wire shape of a sign-in, as the console's own client reads it.</summary>
public sealed record SignInBody(string AccessToken, DateTimeOffset ExpiresAt, UserBody User);

public sealed record ServiceTokenBody(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record UserBody(
    int Id,
    string Username,
    string DisplayName,
    string Role,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastSignedInAt);
