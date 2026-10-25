using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>
/// What the rail says about alerts, on every screen: how many are open, and whether any of
/// them is an empty shelf.
///
/// The count is the service's own total, not the length of the page it returned — the rail
/// must not under-report because it only read the first few rows.
/// </summary>
public sealed record AttentionNotice(int ActiveCount, bool AnyOutOfStock, string? Unavailable = null)
{
    public static AttentionNotice From(PagedResult<AlertDto> alerts) =>
        new(alerts.TotalCount, alerts.Items.Any(alert => alert.Type == AlertType.OUT_OF_STOCK));

    /// <summary>
    /// The Alert service could not be read. The rail says so instead of showing a zero, which
    /// would read as "nothing is wrong".
    /// </summary>
    public static AttentionNotice Offline(string service) => new(0, false, service);
}
