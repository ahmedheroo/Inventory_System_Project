namespace InventorySystem.BuildingBlocks.Security;

/// <summary>
/// The names inside a token. Both sides of the wire read these constants, so "the claim the
/// Inventory service checks" and "the claim the Identity service writes" cannot drift apart.
/// </summary>
public static class JwtClaimNames
{
    /// <summary>Who the token is about: the user id as a string.</summary>
    public const string Subject = "sub";

    /// <summary>The sign-in name, which is also the username.</summary>
    public const string Name = "name";

    /// <summary>The human name a console shows next to the sign-out button.</summary>
    public const string DisplayName = "display_name";

    /// <summary>The staff role: viewer, clerk or manager (or service, for a machine account).</summary>
    public const string Role = "role";

    /// <summary>What this token may do, one claim per scope. Services authorise on these, not on roles.</summary>
    public const string Scope = "scope";
}

/// <summary>
/// What a token may do. Roles are the console's vocabulary for people and screens; scopes are what a
/// service checks, so a service never has to know how the console arranges its screens.
/// </summary>
public static class JwtScopes
{
    /// <summary>Read stock levels, the ledger and the live view.</summary>
    public const string StockRead = "stock.read";

    /// <summary>Perform stock operations.</summary>
    public const string StockWrite = "stock.write";
}

/// <summary>The role names a token can carry.</summary>
public static class JwtRoles
{
    public const string Viewer = "viewer";
    public const string Clerk = "clerk";
    public const string Manager = "manager";

    /// <summary>A machine account: the console's own relay, never a person.</summary>
    public const string Service = "service";
}

/// <summary>How the services name the things they authorise.</summary>
public static class JwtPolicies
{
    /// <summary>Requires <see cref="JwtScopes.StockRead"/>: the live stock view.</summary>
    public const string StockRead = "stock:read";
}
