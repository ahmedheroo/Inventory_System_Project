using InventorySystem.Products.Application.Dtos;
using InventorySystem.Products.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Products.Api.Controllers;

/// Warehouse endpoints
[ApiController]
[Route("api/warehouses")]
[Produces("application/json")]
public class WarehousesController : ControllerBase
{
    private readonly WarehouseService _warehouseService;

    public WarehousesController(WarehouseService warehouseService)
    {
        _warehouseService = warehouseService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<WarehouseDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<WarehouseDto>>> GetWarehouses(
        [FromQuery] bool includeInactive,
        CancellationToken cancellationToken) =>
        Ok(await _warehouseService.GetAsync(includeInactive, cancellationToken));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseDto>> GetWarehouseById(int id, CancellationToken cancellationToken) =>
        Ok(await _warehouseService.GetByIdAsync(id, cancellationToken));

    [HttpPost]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<WarehouseDto>> CreateWarehouse(
        [FromBody] CreateWarehouseRequest request,
        CancellationToken cancellationToken)
    {
        var created = await _warehouseService.CreateAsync(request, cancellationToken);

        return CreatedAtAction(nameof(GetWarehouseById), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseDto>> UpdateWarehouse(
        int id,
        [FromBody] UpdateWarehouseRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _warehouseService.UpdateAsync(id, request, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeactivateWarehouse(int id, CancellationToken cancellationToken)
    {
        await _warehouseService.DeactivateAsync(id, cancellationToken);
        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    [ProducesResponseType(typeof(WarehouseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<WarehouseDto>> ActivateWarehouse(int id, CancellationToken cancellationToken) =>
        Ok(await _warehouseService.ActivateAsync(id, cancellationToken));
}
