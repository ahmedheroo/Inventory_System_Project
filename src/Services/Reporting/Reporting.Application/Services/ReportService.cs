using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.Catalog;
using InventorySystem.Reports.Application.Dtos;
using InventorySystem.Reports.Application.Export;
using InventorySystem.Reports.Application.ReadModel;

namespace InventorySystem.Reports.Application.Services;

/// <summary>
/// The four reports of the specification (section 7.4):
///
/// GET /api/reports/inventory    current stock, with value
/// GET /api/reports/low-stock    stock at or below its minimum
/// GET /api/reports/movement     the movement ledger, date/warehouse filtered
/// GET /api/reports/valuation    quantity × unit price, per product/warehouse
///
/// and the same four as downloads (<c>?format=csv|xlsx</c>), which reuse the exact
/// row builders below so a downloaded report cannot disagree with the screen it
/// came from — the only difference is that an export is never paged.
///
/// The read model holds ids (that is what events carry), so every report is
/// enriched from the Product service once per request: names, and the unit price
/// the valuation report multiplies with. Two shapes of query follow from that:
///
/// * stock levels are one row per (product, warehouse) pair, so the filtered set
///   is loaded and paged here — a value column cannot be summed in SQL;
/// * the movement ledger grows with every event, so it is paged and aggregated by
///   the database and never loaded wholesale (except by an export, which asks for
///   all of it on purpose).
/// </summary>
public class ReportService
{
    private readonly IReportingRepository _repository;
    private readonly IProductCatalogClient _catalog;

    public ReportService(IReportingRepository repository, IProductCatalogClient catalog)
    {
        _repository = repository;
        _catalog = catalog;
    }

    // ----------------------------------------------------------------------
    // Reports
    // ----------------------------------------------------------------------

    public async Task<InventoryReport> GetInventoryReportAsync(
        InventoryReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        var rows = await BuildInventoryRowsAsync(normalized, cancellationToken);

        return new InventoryReport(
            Page(rows, normalized.Page, normalized.PageSize),
            Summarize(rows),
            normalized.Page,
            normalized.PageSize,
            rows.Count);
    }

    public async Task<LowStockReport> GetLowStockReportAsync(
        LowStockReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        var rows = await BuildLowStockRowsAsync(normalized, cancellationToken);

        return new LowStockReport(
            Page(rows, normalized.Page, normalized.PageSize),
            Summarize(rows),
            normalized.Page,
            normalized.PageSize,
            rows.Count);
    }

    public async Task<MovementReport> GetMovementReportAsync(
        MovementReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize().Validate();
        var filter = FilterOf(normalized, unpaged: false);

        var page = await _repository.ListMovementsAsync(filter, cancellationToken);
        var totals = await _repository.SumMovementsAsync(filter, cancellationToken);
        var rows = await BuildMovementRowsAsync(page.Items, cancellationToken);

        return new MovementReport(rows, Summarize(totals), page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<ValuationReport> GetValuationReportAsync(
        ValuationReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize();
        var valuation = await BuildValuationRowsAsync(normalized, cancellationToken);

        return new ValuationReport(
            Page(valuation.Rows, normalized.Page, normalized.PageSize),
            valuation.ByWarehouse,
            Summarize(valuation.Rows),
            normalized.Page,
            normalized.PageSize,
            valuation.Rows.Count);
    }

    // ----------------------------------------------------------------------
    // Downloads: the same rows and the same totals as the report, complete rather
    // than paged, handed to whoever asked for the file.
    // ----------------------------------------------------------------------

    public async Task<ExportDocument> ExportInventoryAsync(
        InventoryReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = await BuildInventoryRowsAsync(query.Normalize(), cancellationToken);

        return ReportExports.Inventory(rows, Summarize(rows));
    }

    public async Task<ExportDocument> ExportLowStockAsync(
        LowStockReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var rows = await BuildLowStockRowsAsync(query.Normalize(), cancellationToken);

        return ReportExports.LowStock(rows, Summarize(rows));
    }

    public async Task<ExportDocument> ExportMovementAsync(
        MovementReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalize().Validate();
        var filter = FilterOf(normalized, unpaged: true);

        var movements = await _repository.ListAllMovementsAsync(filter, cancellationToken);
        var totals = await _repository.SumMovementsAsync(filter, cancellationToken);
        var rows = await BuildMovementRowsAsync(movements, cancellationToken);

        return ReportExports.Movement(rows, Summarize(totals));
    }

    public async Task<ExportDocument> ExportValuationAsync(
        ValuationReportQuery query,
        CancellationToken cancellationToken = default)
    {
        var valuation = await BuildValuationRowsAsync(query.Normalize(), cancellationToken);

        return ReportExports.Valuation(valuation.Rows, valuation.ByWarehouse, Summarize(valuation.Rows));
    }

    // ----------------------------------------------------------------------
    // Row builders — one definition per report, shared by report and export
    // ----------------------------------------------------------------------

    private async Task<IReadOnlyList<InventoryReportRow>> BuildInventoryRowsAsync(
        InventoryReportQuery query,
        CancellationToken cancellationToken)
    {
        var stockLevels = await _repository.ListStockLevelsAsync(
            new StockLevelFilter(query.ProductId, query.WarehouseId),
            cancellationToken);

        var catalog = await _catalog.GetSnapshotAsync(cancellationToken);

        return stockLevels
            .Select(stockLevel => new InventoryReportRow(
                stockLevel.ProductId,
                Sku(stockLevel.ProductId, catalog),
                ProductName(stockLevel.ProductId, catalog),
                stockLevel.WarehouseId,
                WarehouseCode(stockLevel.WarehouseId, catalog),
                WarehouseName(stockLevel.WarehouseId, catalog),
                stockLevel.Quantity,
                stockLevel.MinimumStockLevel,
                UnitPrice(stockLevel.ProductId, catalog),
                UnitPrice(stockLevel.ProductId, catalog) * stockLevel.Quantity,
                stockLevel.IsLowStock,
                stockLevel.IsOutOfStock,
                stockLevel.UpdatedAt))
            .ToList();
    }

    private async Task<IReadOnlyList<LowStockReportRow>> BuildLowStockRowsAsync(
        LowStockReportQuery query,
        CancellationToken cancellationToken)
    {
        var stockLevels = await _repository.ListStockLevelsAsync(
            new StockLevelFilter(query.ProductId, query.WarehouseId),
            cancellationToken);

        var catalog = await _catalog.GetSnapshotAsync(cancellationToken);

        return stockLevels
            .Where(stockLevel => stockLevel.IsLowStock)                      // quantity <= minimum
            .Where(stockLevel => query.IncludeOutOfStock || !stockLevel.IsOutOfStock)
            .Select(stockLevel => new LowStockReportRow(
                stockLevel.ProductId,
                Sku(stockLevel.ProductId, catalog),
                ProductName(stockLevel.ProductId, catalog),
                stockLevel.WarehouseId,
                WarehouseCode(stockLevel.WarehouseId, catalog),
                WarehouseName(stockLevel.WarehouseId, catalog),
                stockLevel.Quantity,
                stockLevel.MinimumStockLevel,
                stockLevel.Shortfall,
                stockLevel.IsOutOfStock ? LowStockStatus.OUT_OF_STOCK : LowStockStatus.LOW_STOCK,
                stockLevel.UpdatedAt))
            // Worst first: nothing in stock, then the largest shortfall.
            .OrderByDescending(row => row.Status == LowStockStatus.OUT_OF_STOCK)
            .ThenByDescending(row => row.Shortfall)
            .ThenBy(row => row.ProductId)
            .ThenBy(row => row.WarehouseId)
            .ToList();
    }

    private async Task<IReadOnlyList<MovementReportRow>> BuildMovementRowsAsync(
        IReadOnlyList<StockMovement> movements,
        CancellationToken cancellationToken)
    {
        var catalog = await _catalog.GetSnapshotAsync(cancellationToken);

        return movements
            .Select(movement => new MovementReportRow(
                movement.Id,
                movement.ProductId,
                Sku(movement.ProductId, catalog),
                ProductName(movement.ProductId, catalog),
                movement.WarehouseId,
                WarehouseCode(movement.WarehouseId, catalog),
                WarehouseName(movement.WarehouseId, catalog),
                movement.Type,
                movement.Quantity,
                movement.SignedQuantity,
                movement.QuantityAfter,
                movement.OccurredAt))
            .ToList();
    }

    private async Task<(IReadOnlyList<ValuationReportRow> Rows, IReadOnlyList<ValuationWarehouseTotal> ByWarehouse)>
        BuildValuationRowsAsync(ValuationReportQuery query, CancellationToken cancellationToken)
    {
        var stockLevels = await _repository.ListStockLevelsAsync(
            new StockLevelFilter(WarehouseId: query.WarehouseId),
            cancellationToken);

        var catalog = await _catalog.GetSnapshotAsync(cancellationToken);

        var rows = stockLevels
            .Where(stockLevel => query.CategoryId is not { } categoryId
                                 || catalog.Product(stockLevel.ProductId)?.CategoryId == categoryId)
            .Select(stockLevel =>
            {
                var unitPrice = UnitPrice(stockLevel.ProductId, catalog);

                return new ValuationReportRow(
                    stockLevel.ProductId,
                    Sku(stockLevel.ProductId, catalog),
                    ProductName(stockLevel.ProductId, catalog),
                    catalog.Product(stockLevel.ProductId)?.CategoryId,
                    stockLevel.WarehouseId,
                    WarehouseCode(stockLevel.WarehouseId, catalog),
                    WarehouseName(stockLevel.WarehouseId, catalog),
                    stockLevel.Quantity,
                    unitPrice,
                    unitPrice * stockLevel.Quantity);
            })
            .OrderByDescending(row => row.Value)
            .ThenBy(row => row.ProductId)
            .ThenBy(row => row.WarehouseId)
            .ToList();

        var byWarehouse = rows
            .GroupBy(row => new { row.WarehouseId, row.WarehouseCode, row.WarehouseName })
            .Select(group => new ValuationWarehouseTotal(
                group.Key.WarehouseId,
                group.Key.WarehouseCode,
                group.Key.WarehouseName,
                group.Sum(row => row.Quantity),
                group.Sum(row => row.Value)))
            .OrderByDescending(total => total.Value)
            .ThenBy(total => total.WarehouseId)
            .ToList();

        return (rows, byWarehouse);
    }

    // ----------------------------------------------------------------------
    // Summaries — the totals shown above each table
    // ----------------------------------------------------------------------

    private static InventoryReportSummary Summarize(IReadOnlyList<InventoryReportRow> rows) => new(
        rows.Select(row => row.ProductId).Distinct().Count(),
        rows.Select(row => row.WarehouseId).Distinct().Count(),
        rows.Count,
        rows.Sum(row => row.Quantity),
        rows.Sum(row => row.Value),
        rows.Count(row => row.IsLowStock),
        rows.Count(row => row.IsOutOfStock));

    private static LowStockReportSummary Summarize(IReadOnlyList<LowStockReportRow> rows) => new(
        rows.Count(row => row.Status == LowStockStatus.LOW_STOCK),
        rows.Count(row => row.Status == LowStockStatus.OUT_OF_STOCK),
        rows.Sum(row => row.Shortfall),
        rows.Select(row => row.ProductId).Distinct().Count(),
        rows.Select(row => row.WarehouseId).Distinct().Count());

    private static MovementReportSummary Summarize(MovementTotals totals) => new(
        totals.TotalMovements,
        totals.TotalReceived,
        totals.TotalIssued,
        totals.TotalTransferred,
        totals.TotalAdjustedUp,
        totals.TotalAdjustedDown,
        totals.NetChange);

    private static ValuationReportSummary Summarize(IReadOnlyList<ValuationReportRow> rows) => new(
        rows.Select(row => row.ProductId).Distinct().Count(),
        rows.Select(row => row.WarehouseId).Distinct().Count(),
        rows.Count,
        rows.Sum(row => row.Quantity),
        rows.Sum(row => row.Value));

    // ----------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------

    private static MovementFilter FilterOf(MovementReportQuery query, bool unpaged) =>
        unpaged
            ? MovementFilter.Unpaged(query.ProductId, query.WarehouseId, query.Type, query.From, query.To)
            : new MovementFilter(
                query.ProductId,
                query.WarehouseId,
                query.Type,
                query.From,
                query.To,
                query.Page,
                query.PageSize);

    /// <summary>Slices the filtered rows into the requested page (never past the end).</summary>
    private static IReadOnlyList<T> Page<T>(IReadOnlyList<T> rows, int page, int pageSize) =>
        rows.Count == 0
            ? Array.Empty<T>()
            : rows.Skip((page - 1) * pageSize).Take(pageSize).ToList();

    // The read model is kept free of catalogue data: names and prices are resolved
    // here, with an explicit fallback for an id the Product service no longer knows.

    private static string Sku(int productId, CatalogSnapshot catalog) =>
        catalog.Product(productId)?.Sku ?? $"#{productId}";

    private static string ProductName(int productId, CatalogSnapshot catalog) =>
        catalog.Product(productId)?.Name ?? $"Unknown product {productId}";

    private static string WarehouseCode(int warehouseId, CatalogSnapshot catalog) =>
        catalog.Warehouse(warehouseId)?.Code ?? $"#{warehouseId}";

    private static string WarehouseName(int warehouseId, CatalogSnapshot catalog) =>
        catalog.Warehouse(warehouseId)?.Name ?? $"Unknown warehouse {warehouseId}";

    private static decimal UnitPrice(int productId, CatalogSnapshot catalog) =>
        catalog.Product(productId)?.UnitPrice ?? 0m;
}
