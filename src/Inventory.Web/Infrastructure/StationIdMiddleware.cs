namespace Inventory.Web.Infrastructure;

/// <summary>
/// Names the browser a request came from.
///
/// Several people work the same system at once — a clerk on the dock terminal, a manager in the
/// office — and a live screen that says "something moved" is less useful than one that says which
/// of those movements was yours. Until sign-in exists, the closest thing to an identity is the
/// browser, so the console mints an opaque station id per browser, remembers it in a cookie, and
/// sends it with the stock operations that browser performs.
///
/// It is a *label*, not a credential: nothing is authorised by it, and the id carries no data about
/// the machine or the person using it.
/// </summary>
public sealed class StationIdMiddleware
{
    /// <summary>The cookie that remembers which station a browser is.</summary>
    public const string CookieName = "inventory.station";

    /// <summary>Where the station is put for the rest of the request, for the station service to read.</summary>
    public const string ItemKey = "inventory.station";

    private const int Length = 8;

    private readonly RequestDelegate _next;

    public StationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var remembered = Plausible(context.Request.Cookies[CookieName]);
        var station = remembered ?? Mint();

        context.Items[ItemKey] = station;

        if (remembered is null)
        {
            context.Response.Cookies.Append(
                CookieName,
                station,
                new CookieOptions
                {
                    HttpOnly = true,                      // nothing in the browser needs to read it
                    SameSite = SameSiteMode.Lax,
                    IsEssential = true,                   // it exists so the console can tell two terminals apart
                    Secure = context.Request.IsHttps
                });
        }

        await _next(context);
    }

    /// <summary>A station id that could have been minted here, or null: a hand-edited cookie is replaced, not trusted.</summary>
    private static string? Plausible(string? cookie) =>
        cookie is { Length: Length } && cookie.All(Uri.IsHexDigit) ? cookie : null;

    private static string Mint() => Guid.NewGuid().ToString("N")[..Length];
}
