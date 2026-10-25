using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>One warehouse column on the overview: what it holds, what it is worth, what is short in it.</summary>
public sealed record WarehousePosition(
    WarehouseDto Warehouse,
    ValuationWarehouseTotal? Total,
    IReadOnlyList<LowStockReportRow> Shortest)
{
    public int StockLevels { get; init; }
}

/// <summary>
/// The overview. It answers the manager's first question — where am I short — with the
/// per-warehouse position, then lists what needs attention and what just happened.
/// </summary>
public sealed class DashboardViewModel
{
    public ValuationReportSummary Value { get; init; } = new(0, 0, 0, 0, 0m);

    public LowStockReportSummary Shortages { get; init; } = new(0, 0, 0, 0, 0);

    public IReadOnlyList<WarehousePosition> Positions { get; init; } = [];

    /// <summary>The rows in the worst state across every warehouse, for the table under the board.</summary>
    public IReadOnlyList<LowStockReportRow> ShortStock { get; init; } = [];

    public IReadOnlyList<MovementReportRow> RecentMovements { get; init; } = [];

    public IReadOnlyList<AlertDto> OpenAlerts { get; init; } = [];

    /// <summary>How many active alerts there are in total, which may exceed the ones listed.</summary>
    public int OpenAlertCount { get; init; }

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;
}
