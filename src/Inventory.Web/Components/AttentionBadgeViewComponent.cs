using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Components;


public sealed class AttentionBadgeViewComponent : ViewComponent
{

    private const int Scanned = 20;

    private readonly IAlertsApi _alerts;
    private readonly ILogger<AttentionBadgeViewComponent> _logger;

    public AttentionBadgeViewComponent(IAlertsApi alerts, ILogger<AttentionBadgeViewComponent> logger)
    {
        _alerts = alerts;
        _logger = logger;
    }

    public async Task<IViewComponentResult> InvokeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var alerts = await _alerts.GetAlertsAsync(
                new AlertQuery { Status = AlertStatus.ACTIVE, Page = 1, PageSize = Scanned },
                cancellationToken);

            return View(AttentionNotice.From(alerts));
        }
        catch (ApiException exception)
        {
            // One service being down must not take the rail down on every screen of the
            // console: the badge reports what it could not read instead.
            _logger.LogWarning(exception, "The rail could not read the open alerts.");

            return View(AttentionNotice.Offline(exception.Service));
        }
    }
}
