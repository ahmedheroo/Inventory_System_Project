namespace Inventory.Web.Api.Contracts;


public sealed record LoginRequest(string Username, string Password);

public sealed record IdentityUser(int Id, string Username, string DisplayName, string Role);

public sealed record SignInResponse(string AccessToken, DateTime ExpiresAt, IdentityUser User);

public sealed record ClientCredentialsRequest(string ClientId, string ClientSecret);

public sealed record ServiceTokenResponse(string AccessToken, DateTime ExpiresAt);
