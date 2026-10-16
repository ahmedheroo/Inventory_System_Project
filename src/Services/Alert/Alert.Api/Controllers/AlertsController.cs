using InventorySystem.Alerts.Application.Dtos;
using InventorySystem.Alerts.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Alerts.Api.Controllers;

/// <summary>
/// Endpoints from the specification (section 7.3):
///
/// GET /api/alerts              list alerts (filter by product, warehouse, type,
///                              read state, status; paged, newest first)
/// GET /api/alerts/{id}         one alert
/// PUT /api/alerts/{id}/read    mark an alert as read (idempotent)
///
/// Alerts are created by the consumer from inventory events, so there is no
/// create endpoint.
/// </summary>
[ApiController]
[Route("api/alerts")]
[Produces("application/json")]
public class AlertsController : ControllerBase
{
    private readonly AlertService _alertService;

    public AlertsController(AlertService alertService)
    {
        _alertService = alertService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AlertDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<AlertDto>>> GetAlerts(
        [FromQuery] AlertQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _alertService.GetAsync(query, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AlertDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlertDto>> GetAlert(int id, CancellationToken cancellationToken) =>
        Ok(await _alertService.GetByIdAsync(id, cancellationToken));

    [HttpPut("{id:int}/read")]
    [ProducesResponseType(typeof(AlertDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AlertDto>> MarkAsRead(int id, CancellationToken cancellationToken) =>
        Ok(await _alertService.MarkAsReadAsync(id, cancellationToken));
}
