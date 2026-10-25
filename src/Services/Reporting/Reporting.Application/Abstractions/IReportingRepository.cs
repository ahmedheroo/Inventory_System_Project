using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Reports.Application.Dtos;
using InventorySystem.Reports.Application.ReadModel;

namespace InventorySystem.Reports.Application.Abstractions;

/// <summary>
/// The read-model filter of the current inventory / low-stock / valuation
/// reports. Stock levels are one row per (product, warehouse) pair, so the whole
/// filtered set is loaded and paged in the application after the catalogue has
/// been applied (a value column cannot be summed in SQL).
/// </summary>
public sealed record StockLevelFilter(int? ProductId = null, int? WarehouseId = null);

/// <summary>
/// The filter and paging of the movement report, including the date range
/// (TASK-062). The ledger grows with every event, so it is filtered, paged and
/// aggregated by the database.
/// </summary>
public sealed record MovementFilter(
    int? ProductId = null,
    int? WarehouseId = null,
    StockTransactionType? Type = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20)
{
    /// <summary>
    /// The same filter with paging switched off, for an export that must contain
    /// every matching movement. It is a named method rather than a magic page size
    /// so a report page cannot ask for the whole ledger by accident.
    /// </summary>
    public static MovementFilter Unpaged(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null) => new(productId, warehouseId, type, from, to, Page: 1, PageSize: 0);
}

/// <summary>
/// Aggregates over the whole filtered movement set, not only the page.
/// <see cref="TotalAdjustedUp"/> and <see cref="TotalAdjustedDown"/> are split
/// because an adjustment can go either way, and <see cref="NetChange"/> is the
/// signed sum of everything (+in / -out).
/// </summary>
public sealed record MovementTotals(
    int TotalMovements,
    int TotalReceived,
    int TotalIssued,
    int TotalTransferred,
    int TotalAdjustedUp,
    int TotalAdjustedDown,
    int NetChange);

/// <summary>
/// The Reporting service's read-model store: the queries the report endpoints use
/// and the writes the projection performs. Keeping it behind an interface keeps
/// the Application layer persistence-ignorant (architecture decision 6).
/// </summary>
public interface IReportingRepository
{
    // ---- queries -----------------------------------------------------------

    /// <summary>Every stock level matching the filter, ordered by product then warehouse.</summary>
    Task<IReadOnlyList<StockLevelSnapshot>> ListStockLevelsAsync(
        StockLevelFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>One page of the movement ledger, newest first, plus the filtered total.</summary>
    Task<PagedResult<StockMovement>> ListMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every movement matching the filter, in the same order as the paged list.
    /// Only the exports call this: the ledger is unbounded, so an export is the
    /// one place where "all of it" is the intended answer (and the date filters are
    /// how a caller keeps it small).
    /// </summary>
    Task<IReadOnlyList<StockMovement>> ListAllMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default);

    /// <summary>Aggregates computed by the database over the whole filtered movement set.</summary>
    Task<MovementTotals> SumMovementsAsync(
        MovementFilter filter,
        CancellationToken cancellationToken = default);

    // ---- projection --------------------------------------------------------

    /// <summary>The projected stock level of a (product, warehouse) pair, if it exists.</summary>
    Task<StockLevelSnapshot?> FindStockLevelAsync(
        int productId,
        int warehouseId,
        CancellationToken cancellationToken = default);

    void AddStockLevel(StockLevelSnapshot stockLevel);

    void AddMovement(StockMovement movement);
}

/// <summary>
/// Commits one consumed event's projection — its stock-level upserts, its
/// movements and its de-duplication record — in a single transaction.
/// </summary>
public interface IUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
