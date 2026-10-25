using System.Globalization;
using System.Security.Claims;
using Inventory.Web.Api.Contracts;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace Inventory.Web.Security;

/// <summary>
/// The console's own session.
///
/// Signing in asks the Identity service whether a username and a password are an account, and then
/// keeps what it learned as a cookie of its own: the account's id, sign-in name, display name and
/// role. The token that came back is deliberately *not* kept. A person's token is for a service to
/// read, and the console is not one; what the browser holds is a session it can be signed out of,
/// and what the console sends to the Inventory service is the console's own credential (see
/// <c>ServiceTokenProvider</c>), not the person's.
/// </summary>
public static class ConsoleSession
{
    public const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    /// <summary>Namespaced so it cannot be confused with the station cookie, which is a label, not a session.</summary>
    public const string CookieName = "inventory.console";

    /// <summary>A working day. Signing out is instant; this is the limit on a session nobody closed.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);

    /// <summary>
    /// The paths that are called by the live client rather than navigated to by a person. They are
    /// answered with a status code instead of a redirect to the sign-in page.
    /// </summary>
    public static readonly string[] StatusPaths = ["/live", "/hubs"];

    /// <summary>
    /// What the cookie carries. The role goes in under the standard claim name so the framework's own
    /// role checks — and every policy in <see cref="ConsoleAuthorisation"/> — read it without a
    /// custom claim type being threaded through every call site.
    /// </summary>
    public static ClaimsPrincipal PrincipalFor(SignInResponse signedIn) =>
        new(new ClaimsIdentity(
            [
                new Claim(ConsoleClaims.Subject, signedIn.User.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, signedIn.User.Username),
                new Claim(ConsoleClaims.DisplayName, signedIn.User.DisplayName),
                new Claim(ClaimTypes.Role, signedIn.User.Role)
            ],
            Scheme,
            ClaimTypes.Name,
            ClaimTypes.Role));

    public static void ConfigureCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = CookieName;
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;      // one origin: Lax still carries the sign-in redirect
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
        options.Cookie.IsEssential = true;
        options.LoginPath = "/sign-in";
        options.AccessDeniedPath = "/sign-in/denied";
        options.ReturnUrlParameter = "returnUrl";
        options.ExpireTimeSpan = Lifetime;
        options.SlidingExpiration = false;
        options.Events = new CookieAuthenticationEvents
        {
            // A page gets the sign-in screen. The live regions and the hub get a status code, because
            // handing fetch() an HTML sign-in page would put a login form inside the table it was
            // refreshing — and a live client that cannot tell "you are signed out" from "here is your
            // data" is worse than one that simply fails.
            OnRedirectToLogin = context => AnswerAsync(context, StatusCodes.Status401Unauthorized),
            OnRedirectToAccessDenied = context => AnswerAsync(context, StatusCodes.Status403Forbidden)
        };
    }

    /// <summary>Whether this request came from the live client rather than from a person following a link.</summary>
    public static bool WantsStatusCode(PathString path) =>
        StatusPaths.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase));

    private static Task AnswerAsync(RedirectContext<CookieAuthenticationOptions> context, int statusCode)
    {
        if (WantsStatusCode(context.Request.Path))
        {
            context.Response.StatusCode = statusCode;

            return Task.CompletedTask;
        }

        context.Response.Redirect(context.RedirectUri);

        return Task.CompletedTask;
    }
}
