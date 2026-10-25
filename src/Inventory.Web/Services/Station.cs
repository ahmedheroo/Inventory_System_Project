using Inventory.Web.Infrastructure;

namespace Inventory.Web.Services;

/// <summary>Which browser this request belongs to, as worked out by <see cref="StationIdMiddleware"/>.</summary>
public interface IStation
{
    /// <summary>The station's short label, or null when the request has none (a background context).</summary>
    string? Id { get; }
}

/// <summary>
/// Reads the station off the request. Two callers need it: the handler that puts it on the calls to
/// the Inventory service, and the layout that hands it to the browser so a screen can recognise its
/// own movements coming back on the hub.
/// </summary>
public sealed class StationAccessor : IStation
{
    private readonly IHttpContextAccessor _accessor;

    public StationAccessor(IHttpContextAccessor accessor)
    {
        _accessor = accessor;
    }

    public string? Id => _accessor.HttpContext?.Items[StationIdMiddleware.ItemKey] as string;
}
