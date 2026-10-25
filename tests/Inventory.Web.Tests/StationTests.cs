using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Infrastructure;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Http;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The station a browser is, and the label the console puts on the operations it performs.
///
/// Two things matter. The station has to be stable for a browser (or a screen would not recognise
/// its own movements), and it has to be *plausible*: a cookie is part of the request, so a
/// hand-edited one is replaced rather than echoed into a live notification everybody can see. The
/// header goes on writes only — a page read has no movement to claim.
/// </summary>
public class StationTests
{
    private static async Task<HttpContext> Through(HttpContext context)
    {
        var middleware = new StationIdMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        return context;
    }

    private static HttpContext Request(string? cookie = null)
    {
        var context = new DefaultHttpContext();

        if (cookie is not null)
        {
            context.Request.Headers.Cookie = $"{StationIdMiddleware.CookieName}={cookie}";
        }

        return context;
    }

    private static string? StationOf(HttpContext context) =>
        context.Items[StationIdMiddleware.ItemKey] as string;

    private static string? CookieSet(HttpContext context) =>
        context.Response.Headers.SetCookie
            .ToString()
            .Split(';')
            .FirstOrDefault(part => part.StartsWith(StationIdMiddleware.CookieName + "=", StringComparison.Ordinal))
            ?.Split('=', 2)[1];

    [Fact]
    public async Task A_browser_is_given_a_station_the_first_time_it_asks_for_a_page()
    {
        var context = await Through(Request());

        var station = Assert.IsType<string>(StationOf(context));

        Assert.Equal(8, station.Length);
        Assert.All(station, character => Assert.True(Uri.IsHexDigit(character)));
        Assert.Equal(station, CookieSet(context));
    }

    [Fact]
    public async Task A_browser_keeps_the_station_it_already_has()
    {
        var context = await Through(Request("a1b2c3d4"));

        Assert.Equal("a1b2c3d4", StationOf(context));
        Assert.Null(CookieSet(context));   // nothing to write: the browser already knows
    }

    [Theory]
    [InlineData("short")]
    [InlineData("not-hex-and-too-long-to-be-one")]
    [InlineData("zzzzzzzz")]
    public async Task A_cookie_that_could_not_have_been_minted_here_is_replaced(string cookie)
    {
        var context = await Through(Request(cookie));

        var station = Assert.IsType<string>(StationOf(context));

        Assert.NotEqual(cookie, station);
        Assert.Equal(8, station.Length);
        Assert.Equal(station, CookieSet(context));
    }

    [Fact]
    public void The_station_service_reads_what_the_middleware_put_on_the_request()
    {
        var context = new DefaultHttpContext();
        context.Items[StationIdMiddleware.ItemKey] = "9f8e7d6c";

        var station = new StationAccessor(new HttpContextAccessor { HttpContext = context });

        Assert.Equal("9f8e7d6c", station.Id);
    }

    [Fact]
    public void A_request_with_no_station_reports_none_rather_than_guessing()
    {
        var station = new StationAccessor(new HttpContextAccessor { HttpContext = new DefaultHttpContext() });

        Assert.Null(station.Id);
    }

    // ------------------------------------------------------- the attribution

    private sealed class Captured : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    private static async Task<HttpRequestMessage> Sent(HttpMethod method, string? station)
    {
        var captured = new Captured();

        using var handler = new StationHeaderHandler(new FakeStation(station)) { InnerHandler = captured };

        using var client = new HttpClient(handler);

        using var request = new HttpRequestMessage(method, "http://localhost:5102/api/inventory/receive");

        await client.SendAsync(request);

        return captured.Request!;
    }

    [Fact]
    public async Task An_operation_carries_the_station_that_asked_for_it()
    {
        var request = await Sent(HttpMethod.Post, "4f2ab19c");

        Assert.Equal(["4f2ab19c"], request.Headers.GetValues(StationHeaderHandler.HeaderName));
    }

    [Fact]
    public async Task A_read_is_not_attributed()
    {
        var request = await Sent(HttpMethod.Get, "4f2ab19c");

        Assert.False(request.Headers.Contains(StationHeaderHandler.HeaderName));
    }

    [Fact]
    public async Task Without_a_station_nothing_is_sent()
    {
        var request = await Sent(HttpMethod.Post, station: null);

        Assert.False(request.Headers.Contains(StationHeaderHandler.HeaderName));
    }

    private sealed class FakeStation : IStation
    {
        public FakeStation(string? id)
        {
            Id = id;
        }

        public string? Id { get; }
    }
}
