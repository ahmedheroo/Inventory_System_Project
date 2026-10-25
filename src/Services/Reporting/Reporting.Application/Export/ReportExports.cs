using System.Globalization;
using InventorySystem.Reports.Application.Dtos;

namespace InventorySystem.Reports.Application.Export;

/// <summary>
/// Turns each report's rows into the format-neutral export document: the columns,
/// their kinds (so numbers stay numbers in a spreadsheet) and the totals that were
/// shown above the table on screen.
///
/// The exports deliberately mirror what the endpoint displays — the same rows, the
/// same order, the same totals — because a downloaded report that disagrees with
/// the screen it came from is worse than no download.
/// </summary>
public static class ReportExports
{
    public static ExportDocument Inventory(
        IReadOnlyList<InventoryReportRow> rows,
        InventoryReportSummary summary) => new(
        "Inventory report",
        [
            new ExportTable(
                "Inventory",
                [
                    new("Product id", ExportColumnKind.Integer),
                    new("Sku"),
                    new("Product"),
                    new("Warehouse id", ExportColumnKind.Integer),
                    new("Warehouse code"),
                    new("Warehouse"),
                    new("Quantity", ExportColumnKind.Integer),
                    new("Minimum", ExportColumnKind.Integer),
                    new("Unit price", ExportColumnKind.Decimal),
                    new("Value", ExportColumnKind.Decimal),
                    new("Low stock"),
                    new("Out of stock"),
                    new("Updated at", ExportColumnKind.DateTime)
                ],
                rows.Select(row => (IReadOnlyList<object?>)new object?[]
                {
                    row.ProductId,
                    row.Sku,
                    row.ProductName,
                    row.WarehouseId,
                    row.WarehouseCode,
                    row.WarehouseName,
                    row.Quantity,
                    row.MinimumStockLevel,
                    row.UnitPrice,
                    row.Value,
                    row.IsLowStock ? "yes" : "no",
                    row.IsOutOfStock ? "yes" : "no",
                    row.UpdatedAt
                }).ToList())
        ],
        [
            new ExportSummaryRow("Products", Number(summary.ProductCount)),
            new ExportSummaryRow("Warehouses", Number(summary.WarehouseCount)),
            new ExportSummaryRow("Stock levels", Number(summary.StockLevelCount)),
            new ExportSummaryRow("Total quantity", Number(summary.TotalQuantity)),
            new ExportSummaryRow("Total value", Number(summary.TotalValue)),
            new ExportSummaryRow("Low stock", Number(summary.LowStockCount)),
            new ExportSummaryRow("Out of stock", Number(summary.OutOfStockCount))
        ]);

    public static ExportDocument LowStock(
        IReadOnlyList<LowStockReportRow> rows,
        LowStockReportSummary summary) => new(
        "Low-stock report",
        [
            new ExportTable(
                "Low stock",
                [
                    new("Product id", ExportColumnKind.Integer),
                    new("Sku"),
                    new("Product"),
                    new("Warehouse id", ExportColumnKind.Integer),
                    new("Warehouse code"),
                    new("Warehouse"),
                    new("Quantity", ExportColumnKind.Integer),
                    new("Minimum", ExportColumnKind.Integer),
                    new("Shortfall", ExportColumnKind.Integer),
                    new("Status"),
                    new("Updated at", ExportColumnKind.DateTime)
                ],
                rows.Select(row => (IReadOnlyList<object?>)new object?[]
                {
                    row.ProductId,
                    row.Sku,
                    row.ProductName,
                    row.WarehouseId,
                    row.WarehouseCode,
                    row.WarehouseName,
                    row.Quantity,
                    row.MinimumStockLevel,
                    row.Shortfall,
                    row.Status.ToString(),
                    row.UpdatedAt
                }).ToList())
        ],
        [
            new ExportSummaryRow("Low stock", Number(summary.LowStockCount)),
            new ExportSummaryRow("Out of stock", Number(summary.OutOfStockCount)),
            new ExportSummaryRow("Total shortfall", Number(summary.TotalShortfall)),
            new ExportSummaryRow("Affected products", Number(summary.AffectedProducts)),
            new ExportSummaryRow("Affected warehouses", Number(summary.AffectedWarehouses))
        ]);

    public static ExportDocument Movement(
        IReadOnlyList<MovementReportRow> rows,
        MovementReportSummary summary) => new(
        "Movement report",
        [
            new ExportTable(
                "Movements",
                [
                    new("Id", ExportColumnKind.Integer),
                    new("Occurred at", ExportColumnKind.DateTime),
                    new("Product id", ExportColumnKind.Integer),
                    new("Sku"),
                    new("Product"),
                    new("Warehouse id", ExportColumnKind.Integer),
                    new("Warehouse code"),
                    new("Warehouse"),
                    new("Type"),
                    new("Quantity", ExportColumnKind.Integer),
                    new("Signed quantity", ExportColumnKind.Integer),
                    new("Quantity after", ExportColumnKind.Integer)
                ],
                rows.Select(row => (IReadOnlyList<object?>)new object?[]
                {
                    row.Id,
                    row.OccurredAt,
                    row.ProductId,
                    row.Sku,
                    row.ProductName,
                    row.WarehouseId,
                    row.WarehouseCode,
                    row.WarehouseName,
                    row.Type.ToString(),
                    row.Quantity,
                    row.SignedQuantity,
                    row.QuantityAfter
                }).ToList())
        ],
        [
            new ExportSummaryRow("Movements", Number(summary.TotalMovements)),
            new ExportSummaryRow("Received", Number(summary.TotalReceived)),
            new ExportSummaryRow("Issued", Number(summary.TotalIssued)),
            new ExportSummaryRow("Transferred", Number(summary.TotalTransferred)),
            new ExportSummaryRow("Adjusted up", Number(summary.TotalAdjustedUp)),
            new ExportSummaryRow("Adjusted down", Number(summary.TotalAdjustedDown)),
            new ExportSummaryRow("Net change", Number(summary.NetChange))
        ]);

    public static ExportDocument Valuation(
        IReadOnlyList<ValuationReportRow> rows,
        IReadOnlyList<ValuationWarehouseTotal> byWarehouse,
        ValuationReportSummary summary) => new(
        "Inventory valuation report",
        [
            new ExportTable(
                "Valuation",
                [
                    new("Product id", ExportColumnKind.Integer),
                    new("Sku"),
                    new("Product"),
                    new("Category id", ExportColumnKind.Integer),
                    new("Warehouse id", ExportColumnKind.Integer),
                    new("Warehouse code"),
                    new("Warehouse"),
                    new("Quantity", ExportColumnKind.Integer),
                    new("Unit price", ExportColumnKind.Decimal),
                    new("Value", ExportColumnKind.Decimal)
                ],
                rows.Select(row => (IReadOnlyList<object?>)new object?[]
                {
                    row.ProductId,
                    row.Sku,
                    row.ProductName,
                    row.CategoryId,
                    row.WarehouseId,
                    row.WarehouseCode,
                    row.WarehouseName,
                    row.Quantity,
                    row.UnitPrice,
                    row.Value
                }).ToList()),
            new ExportTable(
                "By warehouse",
                [
                    new("Warehouse id", ExportColumnKind.Integer),
                    new("Warehouse code"),
                    new("Warehouse"),
                    new("Quantity", ExportColumnKind.Integer),
                    new("Value", ExportColumnKind.Decimal)
                ],
                byWarehouse.Select(total => (IReadOnlyList<object?>)new object?[]
                {
                    total.WarehouseId,
                    total.WarehouseCode,
                    total.WarehouseName,
                    total.Quantity,
                    total.Value
                }).ToList())
        ],
        [
            new ExportSummaryRow("Products", Number(summary.ProductCount)),
            new ExportSummaryRow("Warehouses", Number(summary.WarehouseCount)),
            new ExportSummaryRow("Stock levels", Number(summary.StockLevelCount)),
            new ExportSummaryRow("Total quantity", Number(summary.TotalQuantity)),
            new ExportSummaryRow("Total value", Number(summary.TotalValue))
        ]);

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Number(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
