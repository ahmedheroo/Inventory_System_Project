using InventorySystem.Reports.Application.Dtos;
using InventorySystem.Reports.Application.Export;
using InventorySystem.Reports.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace InventorySystem.Reports.Api.Controllers;


/// GET /api/reports/inventory    current stock levels with their value
/// GET /api/reports/low-stock    stock at or below the product's minimum
/// GET /api/reports/movement     the movement ledger (product/warehouse/type/date)
/// GET /api/reports/valuation    quantity × unit price, per product and warehouse
///
/// and the same four as downloads, in CSV or as an Excel workbook:
///
/// GET /api/reports/{report}/export?format=csv|xlsx



[ApiController]
[Route("api/reports")]
[Produces("application/json")]
public class ReportsController : ControllerBase
{
    private readonly ReportService _reportService;

    public ReportsController(ReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("inventory")]
    [ProducesResponseType(typeof(InventoryReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<InventoryReport>> GetInventoryReport(
        [FromQuery] InventoryReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetInventoryReportAsync(query, cancellationToken));

    [HttpGet("low-stock")]
    [ProducesResponseType(typeof(LowStockReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<LowStockReport>> GetLowStockReport(
        [FromQuery] LowStockReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetLowStockReportAsync(query, cancellationToken));

    [HttpGet("movement")]
    [ProducesResponseType(typeof(MovementReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MovementReport>> GetMovementReport(
        [FromQuery] MovementReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetMovementReportAsync(query, cancellationToken));

    [HttpGet("valuation")]
    [ProducesResponseType(typeof(ValuationReport), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ValuationReport>> GetValuationReport(
        [FromQuery] ValuationReportQuery query,
        CancellationToken cancellationToken) =>
        Ok(await _reportService.GetValuationReportAsync(query, cancellationToken));

    // -----------------------------------------------------------------------
    // Downloads
    // -----------------------------------------------------------------------

    // <param name="format">CSV (RFC 4180, UTF-8 with a BOM) or an .xlsx workbook.</param>

    [HttpGet("inventory/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportInventoryReport(
        [FromQuery] InventoryReportQuery query,
        [FromQuery] ExportFormat format = ExportFormat.Csv,
        CancellationToken cancellationToken = default) =>
        Export(await _reportService.ExportInventoryAsync(query, cancellationToken), format, "inventory-report");

    [HttpGet("low-stock/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportLowStockReport(
        [FromQuery] LowStockReportQuery query,
        [FromQuery] ExportFormat format = ExportFormat.Csv,
        CancellationToken cancellationToken = default) =>
        Export(await _reportService.ExportLowStockAsync(query, cancellationToken), format, "low-stock-report");

    [HttpGet("movement/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportMovementReport(
        [FromQuery] MovementReportQuery query,
        [FromQuery] ExportFormat format = ExportFormat.Csv,
        CancellationToken cancellationToken = default) =>
        Export(await _reportService.ExportMovementAsync(query, cancellationToken), format, "movement-report");

    [HttpGet("valuation/export")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ExportValuationReport(
        [FromQuery] ValuationReportQuery query,
        [FromQuery] ExportFormat format = ExportFormat.Csv,
        CancellationToken cancellationToken = default) =>
        Export(await _reportService.ExportValuationAsync(query, cancellationToken), format, "valuation-report");

    private IActionResult Export(ExportDocument document, ExportFormat format, string fileName)
    {
        var file = ReportExporter.ToFile(document, format, fileName, DateTime.UtcNow);

        return File(file.Content, file.ContentType, file.FileName);
    }
}
