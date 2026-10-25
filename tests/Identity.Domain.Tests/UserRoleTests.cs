using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Exceptions;

namespace InventorySystem.Identity.Domain.Tests;

/// <summary>
/// The four role names are a wire format: they are written into tokens and stored in the database, so
/// they are pinned here rather than left to <c>ToString().ToLower()</c> and a hope that nobody
/// renames the enum.
/// </summary>
public class UserRoleTests
{
    [Theory]
    [InlineData(UserRole.Viewer, "viewer")]
    [InlineData(UserRole.Clerk, "clerk")]
    [InlineData(UserRole.Manager, "manager")]
    [InlineData(UserRole.Service, "service")]
    public void A_role_has_one_known_name(UserRole role, string wireName)
    {
        Assert.Equal(wireName, role.WireName());
        Assert.Equal(role, UserRoleExtensions.Parse(wireName));
        Assert.Equal(role, UserRoleExtensions.Parse(wireName.ToUpperInvariant()));
    }

    [Fact]
    public void Every_role_has_its_own_name()
    {
        var names = Enum.GetValues<UserRole>().Select(role => role.WireName()).ToList();

        Assert.Equal(names.Count, names.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(["viewer", "clerk", "manager", "service"], names);
    }

    [Fact]
    public void A_role_this_system_does_not_know_is_a_rule_violation_not_a_default()
    {
        // Silently treating an unknown role as a viewer would turn a typo in a seed file or a request
        // body into an account with permissions nobody chose.
        var refusal = Assert.Throws<DomainException>(() => UserRoleExtensions.Parse("supervisor"));

        Assert.Contains("viewer, clerk, manager, service", refusal.Message, StringComparison.Ordinal);
        Assert.False(UserRoleExtensions.TryParse("supervisor", out _));
        Assert.False(UserRoleExtensions.TryParse(null, out _));
        Assert.False(UserRoleExtensions.TryParse(string.Empty, out _));
        Assert.False(UserRoleExtensions.TryParse("viewer2", out _));

        Assert.Throws<DomainException>(() => ((UserRole)99).WireName());
    }

    [Fact]
    public void A_name_typed_with_spaces_still_reads()
    {
        Assert.True(UserRoleExtensions.TryParse("  clerk  ", out var role));
        Assert.Equal(UserRole.Clerk, role);
    }
}
