using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.Web.Security;

/// <summary>
/// The roles a console account can hold. The console knows these names as a vocabulary — they arrive
/// on the claim the Identity service writes — and never as a rule about who somebody is: it arranges
/// screens by them, and nothing else.
/// </summary>
public static class ConsoleRoles
{
    public const string Viewer = "viewer";
    public const string Clerk = "clerk";
    public const string Manager = "manager";

    /// <summary>Everyone who may sign in at all.</summary>
    public static readonly string[] Everyone = [Viewer, Clerk, Manager];

    /// <summary>Who may move stock: the dock.</summary>
    public static readonly string[] Operators = [Clerk, Manager];

    /// <summary>What a role is called on screen.</summary>
    public static string LabelOf(string? role) => role switch
    {
        Clerk => "Clerk",
        Manager => "Manager",
        Viewer => "Viewer",
        null or "" => "Signed in",
        _ => role
    };
}

/// <summary>What the console's session cookie carries, besides the sign-in name and the role.</summary>
public static class ConsoleClaims
{
    /// <summary>The account's id at the Identity service, so a screen can name the account.</summary>
    public const string Subject = "sub";

    /// <summary>The person's own name — "Dock clerk", not "clerk".</summary>
    public const string DisplayName = "display_name";
}

/// <summary>How the console names the things it authorises.</summary>
public static class ConsolePolicies
{
    /// <summary>Stock operations and acknowledging an alert.</summary>
    public const string Operate = "console:operate";

    /// <summary>Maintaining the catalogue: products, categories and warehouses.</summary>
    public const string Catalogue = "console:catalogue";
}

/// <summary>
/// The same three questions the policies ask, asked of a principal a screen already holds. A view
/// needs to know what to draw — a rail without the operations group, a list without a delete button —
/// and drawing a control the action would refuse is a worse answer than not drawing it.
/// </summary>
public static class ConsoleAccess
{
    /// <summary>May this account move stock and acknowledge alerts?</summary>
    public static bool CanOperate(this ClaimsPrincipal user) => user.IsInRole(ConsoleRoles.Clerk) || user.IsInRole(ConsoleRoles.Manager);

    /// <summary>May this account maintain the catalogue?</summary>
    public static bool CanMaintainCatalogue(this ClaimsPrincipal user) => user.IsInRole(ConsoleRoles.Manager);

    /// <summary>What to put beside the sign-out button.</summary>
    public static string SignedInAs(this ClaimsPrincipal user) =>
        user.FindFirst(ConsoleClaims.DisplayName)?.Value
        ?? user.Identity?.Name
        ?? "Signed in";

    /// <summary>The role, as a person reads it.</summary>
    public static string RoleLabel(this ClaimsPrincipal user) =>
        ConsoleRoles.LabelOf(user.FindFirst(ClaimTypes.Role)?.Value);
}

public static class ConsoleAuthorisation
{
    /// <summary>
    /// Registers the console's two capability policies and the wall itself.
    ///
    /// The fallback policy is the wall: an endpoint is private unless it says otherwise, so a screen
    /// added later is protected by default rather than by remembering. Screens opt out with
    /// <see cref="AllowAnonymousAttribute"/> and say so in one place — the sign-in page, and the two
    /// pages that report what happened to a request.
    /// </summary>
    public static IServiceCollection AddConsoleAuthorisation(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(
                ConsolePolicies.Operate,
                policy => policy.RequireRole(ConsoleRoles.Operators));

            options.AddPolicy(
                ConsolePolicies.Catalogue,
                policy => policy.RequireRole(ConsoleRoles.Manager));

            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

        return services;
    }
}
