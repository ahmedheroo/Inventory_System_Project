using InventorySystem.Identity.Application.Abstractions;
using InventorySystem.Identity.Application.Dtos;
using InventorySystem.Identity.Application.Exceptions;
using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Security;

namespace InventorySystem.Identity.Application.Services;

 
public sealed class SignInService
{
    private const string Refused = "Invalid username or password.";

    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly ITokenIssuer _tokens;
    private readonly TimeProvider _clock;

    public SignInService(
        IUserRepository users,
        PasswordHasher hasher,
        ITokenIssuer tokens,
        TimeProvider clock)
    {
        _users = users;
        _hasher = hasher;
        _tokens = tokens;
        _clock = clock;
    }

    public async Task<SignInResponse> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        var user = await LookupAsync(request.Username, cancellationToken);

        if (user is null)
        {
            // Work that a real check would have done, so an unknown username takes as long to refuse
            // as a wrong password does.
            _hasher.Decoy.Verify(request.Password);

            throw new InvalidCredentialsException(Refused);
        }

        if (!user.IsActive)
        {
            // A disabled account is told apart from a wrong password on purpose: the username was
            // already known to be correct, and a clerk locked out at the start of a shift needs to
            // know that no password would have worked.
            throw new InvalidCredentialsException(
                $"The account '{user.Username}' is disabled. Ask a manager to enable it.");
        }

        if (!user.VerifyPassword(request.Password))
        {
            throw new InvalidCredentialsException(Refused);
        }

        var now = _clock.GetUtcNow();

        // Recorded before the token is written: what the directory shows is a successful sign-in,
        // not an attempt to make one.
        user.RecordSignIn(now.UtcDateTime);
        await _users.SaveChangesAsync(cancellationToken);

        var issued = _tokens.Issue(user, now);

        return new SignInResponse(issued.AccessToken, issued.ExpiresAt.UtcDateTime, UserSummary.From(user));
    }

 
    public async Task<ServiceToken> IssueServiceTokenAsync(
        ServiceTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = await LookupAsync(request.ClientId, cancellationToken);

        if (client is null || !client.IsService || !client.IsActive || !client.VerifyPassword(request.ClientSecret))
        {
            if (client is null || !client.IsService)
            {
                _hasher.Decoy.Verify(request.ClientSecret);
            }

            throw new InvalidCredentialsException("Invalid client credentials.");
        }

        var issued = _tokens.Issue(client, _clock.GetUtcNow());

        return new ServiceToken(issued.AccessToken, issued.ExpiresAt.UtcDateTime);
    }

    /// Reads an account by sign-in name, tolerating a name that could never exist. 
    private async Task<User?> LookupAsync(string? username, CancellationToken cancellationToken) =>
        User.IsPlausibleUsername(username)
            ? await _users.FindByUsernameAsync(User.NormaliseUsername(username), cancellationToken)
            : null;
}
