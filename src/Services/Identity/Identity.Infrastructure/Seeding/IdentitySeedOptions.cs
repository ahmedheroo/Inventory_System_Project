using InventorySystem.Identity.Domain.Entities;
using Microsoft.Extensions.Configuration;

namespace InventorySystem.Identity.Infrastructure.Seeding;

public sealed record IdentitySeedOptions(bool Enabled, string UserPassword, string ServiceSecret)
{
    public const string DefaultUserPassword = "inventory-console-2026";

    public const string DefaultServiceSecret = "console-relay-development-secret";

    public static readonly IReadOnlyList<(string Username, string DisplayName, UserRole Role)> People =
    [
        ("manager", "Office manager", UserRole.Manager),
        ("clerk", "Dock clerk", UserRole.Clerk),
        ("viewer", "Floor viewer", UserRole.Viewer)
    ];

    public static readonly (string Username, string DisplayName, UserRole Role) ServiceAccount =
        ("console", "Console relay", UserRole.Service);

    public static IdentitySeedOptions FromConfiguration(IConfiguration configuration) => new(
        Enabled: bool.TryParse(configuration["Identity:Seed:Enabled"], out var enabled) && enabled,
        UserPassword: Or(configuration["Identity:Seed:UserPassword"], DefaultUserPassword),
        ServiceSecret: Or(configuration["Identity:Seed:ServiceSecret"], DefaultServiceSecret));

    private static string Or(string? configured, string fallback) =>
        string.IsNullOrWhiteSpace(configured) ? fallback : configured;
}
