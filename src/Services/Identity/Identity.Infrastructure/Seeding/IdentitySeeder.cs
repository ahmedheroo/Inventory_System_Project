using InventorySystem.Identity.Application.Abstractions;
using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Security;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Identity.Infrastructure.Seeding;

public sealed class IdentitySeeder
{
    private readonly IUserRepository _users;
    private readonly PasswordHasher _hasher;
    private readonly IdentitySeedOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<IdentitySeeder> _logger;

    public IdentitySeeder(
        IUserRepository users,
        PasswordHasher hasher,
        IdentitySeedOptions options,
        TimeProvider clock,
        ILogger<IdentitySeeder> logger)
    {
        _users = users;
        _hasher = hasher;
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Development seeding is off; the account directory is left as it is.");

            return;
        }

        // Fail loudly at startup rather than at the first sign-in: a seeded password that does not
        // meet the policy would otherwise create an account nobody can use.
        if (!PasswordHasher.IsAcceptable(_options.UserPassword) || !PasswordHasher.IsAcceptable(_options.ServiceSecret))
        {
            throw new InvalidOperationException(
                "Identity:Seed:UserPassword and Identity:Seed:ServiceSecret must each be at least "
                + $"{PasswordHasher.MinimumLength} characters: {PasswordHasher.ExplainPolicy()}");
        }

        var createdAt = _clock.GetUtcNow().UtcDateTime;
        var created = 0;

        foreach (var (username, displayName, role) in IdentitySeedOptions.People)
        {
            if (await CreateIfMissingAsync(username, displayName, role, _options.UserPassword, createdAt, cancellationToken))
            {
                created++;
            }
        }

        var (serviceUsername, serviceDisplayName, serviceRole) = IdentitySeedOptions.ServiceAccount;

        if (await CreateIfMissingAsync(
                serviceUsername,
                serviceDisplayName,
                serviceRole,
                _options.ServiceSecret,
                createdAt,
                cancellationToken))
        {
            created++;
        }

        if (created > 0)
        {
            await _users.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Seeded {Created} development account(s). Sign in as manager, clerk or viewer; the console presents itself as {ServiceAccount}.",
                created,
                serviceUsername);
        }
    }

    private async Task<bool> CreateIfMissingAsync(
        string username,
        string displayName,
        UserRole role,
        string password,
        DateTime createdAt,
        CancellationToken cancellationToken)
    {
        if (await _users.FindByUsernameAsync(username, cancellationToken) is not null)
        {
            return false;
        }

        _users.Add(User.Register(username, displayName, role, _hasher.Hash(password), createdAt));

        return true;
    }
}
