using InventorySystem.Identity.Application.Abstractions;
using InventorySystem.Identity.Application.Dtos;
using InventorySystem.Identity.Application.Exceptions;
using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Exceptions;
using InventorySystem.Identity.Domain.Security;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Identity.Application.Services;

 
public sealed class UserDirectory
{
    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly TimeProvider _clock;
    private readonly ILogger<UserDirectory> _logger;

    public UserDirectory(
        IUserRepository users,
        PasswordHasher hasher,
        TimeProvider clock,
        ILogger<UserDirectory> logger)
    {
        _users = users;
        _hasher = hasher;
        _clock = clock;
        _logger = logger;
    }

    public async Task<IReadOnlyList<UserSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        var users = await _users.ListAsync(cancellationToken);

        return users.Select(UserSummary.From).ToList();
    }

    public async Task<UserSummary?> FindAsync(int id, CancellationToken cancellationToken = default)
    {
        var user = await _users.FindAsync(id, cancellationToken);

        return user is null ? null : UserSummary.From(user);
    }

    public async Task<UserSummary> GetAsync(int id, CancellationToken cancellationToken = default) =>
        await FindAsync(id, cancellationToken) ?? throw new NotFoundException($"No account with id {id}.");

    public async Task<UserSummary> RegisterAsync(
        RegisterUserRequest request,
        CancellationToken cancellationToken = default)
    {
        var username = User.NormaliseUsername(request.Username);
        var role = UserRoleExtensions.Parse(request.Role);

        if (!PasswordHasher.IsAcceptable(request.Password))
        {
            throw new DomainException(PasswordHasher.ExplainPolicy());
        }

        // Checked here for the answer a person reads; the unique index on the column is what
        // actually guarantees it when two requests arrive at once.
        if (await _users.FindByUsernameAsync(username, cancellationToken) is not null)
        {
            throw new DomainException($"The username '{username}' is already taken.");
        }

        var user = User.Register(
            username,
            request.DisplayName,
            role,
            _hasher.Hash(request.Password),
            _clock.GetUtcNow().UtcDateTime);

        _users.Add(user);
        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Created the {Role} account {Username}.", user.Role.WireName(), user.Username);

        return UserSummary.From(user);
    }
 
    /// Enables or disables an account.
    public async Task<UserSummary> SetActiveAsync(
        int id,
        bool active,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindAsync(id, cancellationToken)
                   ?? throw new NotFoundException($"No account with id {id}.");

        if (active)
        {
            user.Enable();
        }
        else
        {
            user.Disable();
        }

        await _users.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("{Action} the account {Username}.", active ? "Enabled" : "Disabled", user.Username);

        return UserSummary.From(user);
    }
}
