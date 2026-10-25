using System.Globalization;
using Inventory.Web.Api;
using Inventory.Web.Hubs;
using Inventory.Web.Infrastructure;
using Inventory.Web.Relay;
using Inventory.Web.Security;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Localization;

var builder = WebApplication.CreateBuilder(args);

// Every page is built from a service API, so the service clients are part of the
// presentation layer's own composition root.
builder.Services.AddServiceApis(builder.Configuration);

// One catalogue read per request, never across requests: a rename or a new product
// has to be visible on the very next screen, and a cache shared by every request would
// quietly show the catalogue as it was when the process started.
builder.Services.AddScoped<ICatalogLookupService, CatalogLookupService>();

// Which browser a request came from, so the movements it performs can be recognised when they
// come back on the live hub. Nothing is authorised by it (see StationIdMiddleware).
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IStation, StationAccessor>();

// The console's own session: a cookie minted here after the Identity service has accepted a
// username and a password (see ConsoleSession — the person's token is not kept).
builder.Services
    .AddAuthentication(ConsoleSession.Scheme)
    .AddCookie(ConsoleSession.ConfigureCookie);

// Which screens a session covers, and the wall that every screen sits behind. The sign-in page
// is the only thing on the anonymous side of it.
builder.Services.AddConsoleAuthorisation();

builder.Services.AddSingleton(TimeProvider.System);

// The live view. The console holds one token of its own for the Inventory service's hub, and one
// relay carries what it hears to the pages open here — a browser never talks to that service.
builder.Services.AddSingleton<IServiceTokenProvider, ServiceTokenProvider>();
builder.Services.AddSingleton<LiveRelayState>();
builder.Services.AddSingleton<ILiveBroadcaster, HubLiveBroadcaster>();
builder.Services.AddSingleton<StockRelay>();
builder.Services.AddHostedService(serviceProvider => serviceProvider.GetRequiredService<StockRelay>());

builder.Services.AddSignalR();

builder.Services
    .AddControllersWithViews(options => options.Filters.Add<ApiExceptionFilter>());

var app = builder.Build();

// One culture for the whole console. A quantity written as "1.234" on one screen and
// "1,234" on the next is a defect, and a decimal typed as "12,50" must not bind as 1250.
var culture = CultureInfo.InvariantCulture;
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(culture),
    SupportedCultures = [culture],
    SupportedUICultures = [culture]
});

// Early on purpose: the station has to exist before anything renders it or sends it on.
app.UseMiddleware<StationIdMiddleware>();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStatusCodePagesWithReExecute("/Home/Status", "?code={0}");

app.UseStaticFiles();
app.UseRouting();

// Between routing and the endpoints, which is where a session has to be read: a screen decides
// what it is made of from the role on the cookie.
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

// The page's live view, mapped on this console: behind the session above, fed by the relay.
app.MapHub<LiveHub>(LiveHub.Path);

app.Run();

/// <summary>Exposed so the console's tests can host it with WebApplicationFactory.</summary>
public partial class Program
{
}
