using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Stock.Api.Controllers;

/// Stock endpoints from the specification (section 7.2):
///
/// GET  /api/inventory                              list current stock (filter + page)
/// GET  /api/inventory/{productId}                  stock of one product across warehouses
/// POST /api/inventory/receive                      add stock
/// POST /api/inventory/issue                        remove stock
/// POST /api/inventory/transfer                     move stock between warehouses
/// POST /api/inventory/adjust                       set an absolute quantity (stock count)
/// GET  /api/inventory/history                      transaction history

[ApiController]
[Route("api/inventory")]
[Produces("application/json")]
public class InventoryController : ControllerBase
{
    private readonly InventoryService _inventoryService;

    public InventoryController(InventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<InventoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InventoryDto>>> GetInventory(
        [FromQuery] InventoryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.GetAsync(query, cancellationToken));

    [HttpGet("{productId:int}")]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<InventoryDto>>> GetInventoryByProduct(
        int productId,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.GetByProductAsync(productId, cancellationToken));

    [HttpGet("history")]
    [ProducesResponseType(typeof(PagedResult<InventoryTransactionDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InventoryTransactionDto>>> GetHistory(
        [FromQuery] HistoryQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.GetHistoryAsync(query, cancellationToken));

    [HttpPost("receive")]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InventoryDto>> ReceiveStock(
        [FromBody] ReceiveStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.ReceiveAsync(request, cancellationToken));

    [HttpPost("issue")]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InventoryDto>> IssueStock(
        [FromBody] IssueStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.IssueAsync(request, cancellationToken));

    [HttpPost("transfer")]
    [ProducesResponseType(typeof(TransferResultDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<TransferResultDto>> TransferStock(
        [FromBody] TransferStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.TransferAsync(request, cancellationToken));

    [HttpPost("adjust")]
    [ProducesResponseType(typeof(InventoryDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InventoryDto>> AdjustStock(
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken) =>
        Ok(await _inventoryService.AdjustAsync(request, cancellationToken));
}
