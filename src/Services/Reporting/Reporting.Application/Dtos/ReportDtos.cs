using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Reports.Application.Exceptions;

namespace InventorySystem.Reports.Application.Dtos;

// ---------------------------------------------------------------------------
// Current inventory report (TASK-058)
// ---------------------------------------------------------------------------

/// <summary>
/// One stock level with the catalogue applied: the read model only knows ids, the
/// Product service supplies sku, name and unit price, which is what turns the row
/// into a valuation-ready one.
/// </summary>
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

/// <summary>Totals over the whole filtered set, not only the returned page.</summary>
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

public sealed record InventoryReportQuery
{
    public int? ProductId { get; init; }

    /// <summary>Warehouse filter (TASK-063).</summary>
    public int? WarehouseId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public InventoryReportQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}

// ---------------------------------------------------------------------------
// Low-stock report (TASK-059)
// ---------------------------------------------------------------------------

/// <summary>One stock level at or below its minimum.</summary>
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

/// <summary>
/// The state the low-stock rule puts a stock level in (spec section 7.3). Serialised
/// as its numeric value, like the alert and transaction enums of the other services.
/// </summary>
public enum LowStockStatus
{
    /// <summary>At or below the minimum, but there is still stock.</summary>
    LOW_STOCK = 0,

    /// <summary>Quantity has reached zero.</summary>
    OUT_OF_STOCK = 1
}

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

public sealed record LowStockReportQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    /// <summary>When false, rows whose quantity is zero are left out.</summary>
    public bool IncludeOutOfStock { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public LowStockReportQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}

// ---------------------------------------------------------------------------
// Stock movement report (TASK-060, TASK-062, TASK-063)
// ---------------------------------------------------------------------------

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

public sealed record MovementReportQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public StockTransactionType? Type { get; init; }

    /// <summary>Inclusive lower bound on when the stock operation happened.</summary>
    public DateTime? From { get; init; }

    /// <summary>Inclusive upper bound on when the stock operation happened.</summary>
    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public MovementReportQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 200 ? 20 : PageSize,
        From = From?.ToUniversalTime(),
        To = To?.ToUniversalTime()
    };

    /// <summary>Rejects a date range that cannot match anything.</summary>
    public MovementReportQuery Validate()
    {
        if (From is { } from && To is { } to && from > to)
        {
            throw new ValidationException(
                $"'from' ({from:O}) must be earlier than or equal to 'to' ({to:O}).");
        }

        return this;
    }
}

// ---------------------------------------------------------------------------
// Inventory valuation report (TASK-061)
// ---------------------------------------------------------------------------

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

/// <summary>Where the value sits, so a valuation report is readable per warehouse.</summary>
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

public sealed record ValuationReportQuery
{
    public int? WarehouseId { get; init; }

    /// <summary>Restricts the valuation to the products of one category.</summary>
    public int? CategoryId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public ValuationReportQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}
