namespace Inventory.Web.Api.Contracts;

/// The console's own account at the Identity service: the client it presents when it asks for a token
/// to open the live stock view with.
///
/// It is not a person's credential and cannot be one: Identity refuses a client-credentials request
/// from an account that is not a service. The development value is published in appsettings for the
/// seeded development account, and a deployment supplies its own
/// (<c>Services__IdentityApi__ClientSecret</c>).

public sealed record ServiceCredentialOptions(string ClientId, string ClientSecret)
{
    public const string DefaultClientId = "console";

    public const string DefaultClientSecret = "console-relay-development-secret";

    public static ServiceCredentialOptions FromConfiguration(IConfiguration configuration) => new(
        Or(configuration["Services:IdentityApi:ClientId"], DefaultClientId),
        Or(configuration["Services:IdentityApi:ClientSecret"], DefaultClientSecret));

    private static string Or(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
