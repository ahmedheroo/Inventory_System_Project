using Inventory.Web.Services;

namespace Inventory.Web.Api;

/// <summary>
/// Puts the station on the way out. Every stock operation the console sends to the Inventory service
/// carries <c>X-Origin-Id</c>, which comes back on the live notification — that is how a screen can
/// tell "your receipt was confirmed" from "another terminal moved something".
///
/// Reads are left alone: a page read has no movement to claim, and the header is only ever a label.
/// </summary>
public sealed class StationHeaderHandler : DelegatingHandler
{
    public const string HeaderName = "X-Origin-Id";

    private readonly IStation _station;

    public StationHeaderHandler(IStation station)
    {
        _station = station;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_station.Id is { } station
            && request.Method != HttpMethod.Get
            && request.Method != HttpMethod.Head)
        {
            request.Headers.TryAddWithoutValidation(HeaderName, station);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
