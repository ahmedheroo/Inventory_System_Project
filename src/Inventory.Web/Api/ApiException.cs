using System.Net;

namespace Inventory.Web.Api;

/// <summary>
/// A service call that did not succeed: the service was unreachable, answered a
/// problem+json error, or refused the request. The console turns this into a page
/// that names the service, says what it reported and offers the way back, instead
/// of showing an empty table that looks like "no data".
/// </summary>
public class ApiException : Exception
{
    public ApiException(HttpStatusCode statusCode, string service, string message, string? detail = null)
        : base(message)
    {
        StatusCode = statusCode;
        Service = service;
        Detail = detail;
    }

    /// <summary>What the service answered, or 503 when it could not be reached at all.</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>The service that failed, as the console names it.</summary>
    public string Service { get; }

    /// <summary>The problem detail, when the service supplied one.</summary>
    public string? Detail { get; }

    public bool IsValidationFailure =>
        StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Conflict;

    public bool IsNotFound => StatusCode == HttpStatusCode.NotFound;

    /// <summary>The service refused because the record already exists (a duplicate sku, code or name).</summary>
    public bool IsConflict => StatusCode == HttpStatusCode.Conflict;

    /// <summary>The sentence the console shows a person.</summary>
    public string Explain() => StatusCode switch
    {
        HttpStatusCode.NotFound => Detail ?? $"The {Service} has no record with that id.",
        HttpStatusCode.BadRequest or HttpStatusCode.Conflict => Detail ?? $"The {Service} refused the request.",
        HttpStatusCode.ServiceUnavailable => $"The {Service} could not be reached. Start it, then reload this page. ({Detail})",
        _ => Detail ?? $"The {Service} answered {(int)StatusCode}."
    };
}

/// <summary>
/// The base addresses of the service APIs the console talks to. They come from
/// configuration (<c>Services:*:BaseUrl</c>) so a deployment can point the console
/// at another host without a rebuild.
/// </summary>
/// <param name="InventoryHub">
/// Where the console's relay opens the live stock hub. It defaults to the Inventory service's own
/// address — but it is configured separately (<c>Services:InventoryApi:HubUrl</c>) because the
/// address this server can reach is not always the address a browser would be given.
/// </param>
/// <param name="IdentityApi">
/// Where accounts and tokens come from. The console asks it two questions: is this username and
/// password an account, and here are my own credentials, may I have a token.
/// </param>
public sealed record ServiceApiOptions(
    Uri ProductApi,
    Uri InventoryApi,
    Uri AlertApi,
    Uri ReportingApi,
    Uri InventoryHub,
    Uri IdentityApi)
{
    public static ServiceApiOptions FromConfiguration(IConfiguration configuration)
    {
        var inventoryApi = BaseUrlOf(configuration, "InventoryApi", "http://localhost:5102");

        return new ServiceApiOptions(
            BaseUrlOf(configuration, "ProductApi", "http://localhost:5251"),
            inventoryApi,
            BaseUrlOf(configuration, "AlertApi", "http://localhost:5212"),
            BaseUrlOf(configuration, "ReportingApi", "http://localhost:5312"),
            HubUrlOf(configuration, inventoryApi),
            BaseUrlOf(configuration, "IdentityApi", "http://localhost:5402"));
    }

    private static Uri HubUrlOf(IConfiguration configuration, Uri inventoryApi)
    {
        var configured = configuration["Services:InventoryApi:HubUrl"];

        // A hub is an address, not a base: it is negotiated at <url>/negotiate, so no
        // trailing slash is added — a doubled slash inside a URL is the kind of thing that
        // works through one proxy and not the next.
        return string.IsNullOrWhiteSpace(configured)
            ? new Uri(inventoryApi, "hubs/stock")
            : new Uri(configured.TrimEnd('/'));
    }

    private static Uri BaseUrlOf(IConfiguration configuration, string service, string fallback)
    {
        var configured = configuration[$"Services:{service}:BaseUrl"];

        return new Uri((string.IsNullOrWhiteSpace(configured) ? fallback : configured).TrimEnd('/') + "/");
    }
}
