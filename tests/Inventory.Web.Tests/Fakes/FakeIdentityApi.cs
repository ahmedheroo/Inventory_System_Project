using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;

namespace InventorySystem.Web.Tests.Fakes;

/// <summary>
/// The Identity service, faked: the accounts the console may sign in as, and the console's own token
/// for the live view. Both answers have to be taken seriously here — a refusal is the only thing that
/// keeps a session from being minted, and a token is the only thing that opens the stock view.
/// </summary>
public sealed class FakeIdentityApi : IIdentityApi
{
    public List<Account> Accounts { get; } = [];

    public int SignInCalls { get; private set; }

    public int ServiceTokenCalls { get; private set; }

    /// <summary>Set to refuse every sign-in, whatever was typed — what a wrong password looks like here.</summary>
    public ApiException? FailSignInWith { get; set; }

    public ApiException? FailServiceTokenWith { get; set; }

    /// <summary>When the next token stops working, so a test can walk the clock up to it.</summary>
    public DateTime ExpiresAt { get; set; } = DateTime.UtcNow.AddHours(8);

    public FakeIdentityApi With(string username, string password, string displayName, string role)
    {
        Accounts.Add(new Account(username, password, new IdentityUser(Accounts.Count + 1, username, displayName, role)));

        return this;
    }

    public Task<SignInResponse> SignInAsync(LoginRequest request, CancellationToken cancellationToken = default)
    {
        SignInCalls++;

        if (FailSignInWith is not null)
        {
            throw FailSignInWith;
        }

        var account = Accounts.FirstOrDefault(
            candidate => candidate.Username == request.Username && candidate.Password == request.Password);

        if (account is null)
        {
            throw new ApiException(
                HttpStatusCode.Unauthorized,
                "Identity service",
                "The Identity service answered 401 for /api/identity/login.",
                "Invalid username or password.");
        }

        return Task.FromResult(new SignInResponse("a-signed-in-test-token", ExpiresAt, account.User));
    }

    public Task<ServiceTokenResponse> ServiceTokenAsync(
        ClientCredentialsRequest request,
        CancellationToken cancellationToken = default)
    {
        ServiceTokenCalls++;

        if (FailServiceTokenWith is not null)
        {
            throw FailServiceTokenWith;
        }

        return Task.FromResult(new ServiceTokenResponse("the-console's-own-token", ExpiresAt));
    }

    public sealed record Account(string Username, string Password, IdentityUser User);
}
