using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class AlertsController : Controller
{
    /// How many alerts one "acknowledge all" press covers.
    private const int AcknowledgementBatch = 50;

    private readonly IAlertsApi _alerts;
    private readonly ICatalogLookupService _catalogue;

    public AlertsController(IAlertsApi alerts, ICatalogLookupService catalogue)
    {
        _alerts = alerts;
        _catalogue = catalogue;
    }

    [HttpGet("alerts")]
    public async Task<IActionResult> Index(
        string? status = null,
        string? read = null,
        AlertType? type = null,
        int? warehouseId = null,
        int? productId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        // "Active" is the default view: a resolved alert is history, and history has
        // its own filter.
        var statusFilter = status?.ToLowerInvariant() switch
        {
            "resolved" => AlertStatus.RESOLVED,
            "all" => (AlertStatus?)null,
            _ => AlertStatus.ACTIVE
        };

        var readFilter = read?.ToLowerInvariant() switch
        {
            "read" => true,
            "unread" => false,
            _ => (bool?)null
        };

        var filter = new AlertFilter(statusFilter, type, readFilter, warehouseId, productId, page, pageSize);

        var alerts = await _alerts.GetAlertsAsync(
            new AlertQuery
            {
                ProductId = filter.ProductId,
                WarehouseId = filter.WarehouseId,
                Type = filter.Type,
                IsRead = filter.IsRead,
                Status = filter.Status,
                Page = filter.Page,
                PageSize = filter.PageSize
            },
            cancellationToken);

        var catalogue = await _catalogue.GetAsync(cancellationToken);

        ViewData["Nav"] = "alerts";
        ViewData["Section"] = "Signals";
        ViewData["Note"] = "Raised when stock reaches a product's minimum. Acknowledging an alert records that somebody has seen it; the service resolves it when the stock recovers.";

        return View(new AlertListViewModel
        {
            Page = alerts,
            Filter = filter,
            Catalog = catalogue,
            Pager = new Pager(
                "/alerts",
                new Dictionary<string, string?>
                {
                    ["status"] = status,
                    ["read"] = read,
                    ["type"] = filter.Type?.ToString(),
                    ["warehouseId"] = filter.WarehouseId?.ToString(),
                    ["productId"] = filter.ProductId?.ToString(),
                    ["pageSize"] = filter.PageSize.ToString()
                },
                alerts.Page,
                alerts.PageSize,
                alerts.TotalCount)
        });
    }


    [HttpGet("/live/attention")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult LiveAttention() => ViewComponent("AttentionBadge");

    // Acknowledging is work: reading the alerts is not the same as being the one who answers them.
    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("alerts/{id:int}/read")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkRead(int id, string? back, CancellationToken cancellationToken)
    {
        var alert = await _alerts.MarkAsReadAsync(id, cancellationToken);
        var catalogue = await _catalogue.GetAsync(cancellationToken);

        TempData.Confirm(
            $"Acknowledged: {catalogue.ProductName(alert.ProductId)} is {Display.Quantity(alert.CurrentQuantity)} in " +
            $"{catalogue.WarehouseName(alert.WarehouseId)}, against a minimum of {Display.Quantity(alert.MinimumStockLevel)}.");

        return Redirect(Back(back));
    }

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("alerts/read-all")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllRead(string? back, CancellationToken cancellationToken)
    {
        // One acknowledgement is one call to the Alert service, so the batch is bounded
        // and the page says exactly how much of the list it covered.
        var alerts = await _alerts.GetAlertsAsync(
            new AlertQuery { Status = AlertStatus.ACTIVE, IsRead = false, Page = 1, PageSize = AcknowledgementBatch },
            cancellationToken);

        foreach (var alert in alerts.Items)
        {
            await _alerts.MarkAsReadAsync(alert.Id, cancellationToken);
        }

        var acknowledged = alerts.Items.Count;

        if (acknowledged == 0)
        {
            TempData.Warn("There was nothing unread to acknowledge on this view.");
        }
        else if (alerts.TotalCount > acknowledged)
        {
            TempData.Confirm(
                $"Acknowledged {acknowledged} of {alerts.TotalCount} unread alerts. Acknowledge again for the next batch.");
        }
        else
        {
            TempData.Confirm(
                acknowledged == 1
                    ? "Acknowledged the one unread alert."
                    : $"Acknowledged {acknowledged} unread alerts.");
        }

        return Redirect(Back(back));
    }

    private string Back(string? back) =>
        !string.IsNullOrWhiteSpace(back) && Url.IsLocalUrl(back) && back.StartsWith("/alerts", StringComparison.Ordinal)
            ? back
            : Url.Action(nameof(Index)) ?? "/alerts";
}
