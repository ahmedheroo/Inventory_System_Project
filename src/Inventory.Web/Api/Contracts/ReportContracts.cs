namespace Inventory.Web.Api.Contracts;

/// State the low-stock rule puts a stock level in (serialized as a number).
public enum LowStockStatus
{
    LOW_STOCK = 0,
    OUT_OF_STOCK = 1
}

public sealed record InventoryReportRow(
    int ProductId,
    string Sku,
    string ProductName,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    int Quantity,
    int MinimumStockLevel,
    decimal UnitPrice,
    decimal Value,
    bool IsLowStock,
    bool IsOutOfStock,
    DateTime UpdatedAt);

public sealed record InventoryReportSummary(
    int ProductCount,
    int WarehouseCount,
    int StockLevelCount,
    int TotalQuantity,
    decimal TotalValue,
    int LowStockCount,
    int OutOfStockCount);

public sealed record InventoryReport(
    IReadOnlyList<InventoryReportRow> Items,
    InventoryReportSummary Summary,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}

public sealed record LowStockReportRow(
    int ProductId,
    string Sku,
    string ProductName,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    int Quantity,
    int MinimumStockLevel,
    int Shortfall,
    LowStockStatus Status,
    DateTime UpdatedAt);

public sealed record LowStockReportSummary(
    int LowStockCount,
    int OutOfStockCount,
    int TotalShortfall,
    int AffectedProducts,
    int AffectedWarehouses);

public sealed record LowStockReport(
    IReadOnlyList<LowStockReportRow> Items,
    LowStockReportSummary Summary,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}

public sealed record MovementReportRow(
    int Id,
    int ProductId,
    string Sku,
    string ProductName,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    StockTransactionType Type,
    int Quantity,
    int SignedQuantity,
    int QuantityAfter,
    DateTime OccurredAt);

public sealed record MovementReportSummary(
    int TotalMovements,
    int TotalReceived,
    int TotalIssued,
    int TotalTransferred,
    int TotalAdjustedUp,
    int TotalAdjustedDown,
    int NetChange);

public sealed record MovementReport(
    IReadOnlyList<MovementReportRow> Items,
    MovementReportSummary Summary,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}

public sealed record ValuationReportRow(
    int ProductId,
    string Sku,
    string ProductName,
    int? CategoryId,
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    int Quantity,
    decimal UnitPrice,
    decimal Value);

public sealed record ValuationWarehouseTotal(
    int WarehouseId,
    string WarehouseCode,
    string WarehouseName,
    int Quantity,
    decimal Value);

public sealed record ValuationReportSummary(
    int ProductCount,
    int WarehouseCount,
    int StockLevelCount,
    int TotalQuantity,
    decimal TotalValue);

public sealed record ValuationReport(
    IReadOnlyList<ValuationReportRow> Items,
    IReadOnlyList<ValuationWarehouseTotal> ByWarehouse,
    ValuationReportSummary Summary,
    int Page,
    int PageSize,
    int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);

    public bool HasNextPage => Page < TotalPages;

    public bool HasPreviousPage => Page > 1;
}
