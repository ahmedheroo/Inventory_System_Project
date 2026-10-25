using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Api;

/// <summary>
/// Accounts and tokens. Two calls: one turns a username and password into an account, the other
/// turns the console's own client credentials into a token for the live stock view.
/// </summary>
public interface IIdentityApi
{
    /// <summary>
    /// Signs an account in. A refusal — a wrong password, an account that is not there, a disabled
    /// one — arrives as an <see cref="ApiException"/> answering <c>401</c>, carrying the Identity
    /// service's own sentence so the sign-in page can repeat it rather than invent one.
    /// </summary>
    Task<SignInResponse> SignInAsync(LoginRequest request, CancellationToken cancellationToken = default);

    /// <summary>The console's own token. A refusal is <c>401</c>; an unreachable service is <c>503</c>.</summary>
    Task<ServiceTokenResponse> ServiceTokenAsync(ClientCredentialsRequest request, CancellationToken cancellationToken = default);
}

public sealed class IdentityApi : ApiClientBase, IIdentityApi
{
    public IdentityApi(HttpClient httpClient, ILogger<IdentityApi> logger)
        : base(httpClient, logger, "Identity service")
    {
    }

    public Task<SignInResponse> SignInAsync(LoginRequest request, CancellationToken cancellationToken = default) =>
        PostAsync<SignInResponse>("/api/identity/login", request, cancellationToken);

    public Task<ServiceTokenResponse> ServiceTokenAsync(
        ClientCredentialsRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ServiceTokenResponse>("/api/identity/token", request, cancellationToken);
}
