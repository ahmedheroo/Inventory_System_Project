using InventorySystem.Identity.Domain.Exceptions;

namespace InventorySystem.Identity.Domain.Entities;

 
public enum UserRole
{
    ///  Reads every screen. Changes nothing. 
    Viewer = 0,

    ///  Reads, and performs the stock operations a dock clerk performs. 
    Clerk = 1,

    ///  Reads and operates, and maintains the catalogue. 
    Manager = 2,

    /// A machine account, not a person: the console's own relay, which follows the live stock
    /// view on behalf of the people signed in to it.
    Service = 3
}

public static class UserRoleExtensions
{
    ///  The name written into the token and stored in the database 
    public static string WireName(this UserRole role) => role switch
    {
        UserRole.Viewer => "viewer",
        UserRole.Clerk => "clerk",
        UserRole.Manager => "manager",
        UserRole.Service => "service",
        _ => throw new DomainException($"'{role}' is not a role this system knows.")
    };

 
    public static UserRole Parse(string? value)
    {
        if (TryParse(value, out var role))
        {
            return role;
        }

        throw new DomainException(
            $"'{value}' is not a role this system knows. Expected one of: viewer, clerk, manager, service.");
    }

    public static bool TryParse(string? value, out UserRole role)
    {
        foreach (var candidate in Enum.GetValues<UserRole>())
        {
            if (string.Equals(candidate.WireName(), value?.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                role = candidate;

                return true;
            }
        }

        role = default;

        return false;
    }
}
