namespace InventorySystem.Reports.Application.ReadModel;

/// <summary>
/// The Reporting service's projection of one (product, warehouse) stock level —
/// the "current inventory report" row, kept up to date by the events the service
/// consumes (spec 7.4, TASK-055 → TASK-058).
///
/// The Reporting service has no domain layer of its own: its model *is* a
/// projection, so the read-model types live in the Application layer instead of a
/// separate Domain project (see docs/architecture.md). The type therefore holds
/// only what an event reports; product and warehouse *names* belong to the
/// Product service and are resolved over HTTP when a report is rendered.
/// </summary>
public class StockLevelSnapshot
{
    // Required by EF Core.
    private StockLevelSnapshot()
    {
    }

    private StockLevelSnapshot(
        int productId,
        int warehouseId,
        int quantity,
        int minimumStockLevel,
        Guid sourceEventId,
        DateTime updatedAt)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Quantity = quantity;
        MinimumStockLevel = minimumStockLevel;
        SourceEventId = sourceEventId;
        UpdatedAt = updatedAt;
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int WarehouseId { get; private set; }

    /// <summary>Quantity on hand, as the newest event for this pair reported it.</summary>
    public int Quantity { get; private set; }

    /// <summary>Minimum stock level the Product service held when the event was published.</summary>
    public int MinimumStockLevel { get; private set; }

    /// <summary>The event that last changed this row (traceability).</summary>
    public Guid SourceEventId { get; private set; }

    /// <summary>Timestamp of that event.</summary>
    public DateTime UpdatedAt { get; private set; }

    /// <summary>The specification's low-stock rule: <c>currentQuantity &lt;= minimumStockLevel</c>.</summary>
    public bool IsLowStock => Quantity <= MinimumStockLevel;

    public bool IsOutOfStock => Quantity == 0;

    /// <summary>Units missing to reach the minimum; meaningful when <see cref="IsLowStock"/>.</summary>
    public int Shortfall => MinimumStockLevel - Quantity;

    public static StockLevelSnapshot Create(
        int productId,
        int warehouseId,
        int quantity,
        int minimumStockLevel,
        Guid sourceEventId,
        DateTime updatedAt)
    {
        Validate(productId, warehouseId, quantity, minimumStockLevel);

        return new StockLevelSnapshot(productId, warehouseId, quantity, minimumStockLevel, sourceEventId, updatedAt);
    }

    /// <summary>
    /// Applies a newer observation of the same (product, warehouse) pair. This is
    /// the upsert the projection performs: one row per pair, never a row per event.
    /// </summary>
    public void Apply(int quantity, int minimumStockLevel, Guid sourceEventId, DateTime updatedAt)
    {
        Validate(ProductId, WarehouseId, quantity, minimumStockLevel);

        Quantity = quantity;
        MinimumStockLevel = minimumStockLevel;
        SourceEventId = sourceEventId;
        UpdatedAt = updatedAt;
    }

    private static void Validate(int productId, int warehouseId, int quantity, int minimumStockLevel)
    {
        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId), productId, "A stock level must reference a product.");
        }

        if (warehouseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId), warehouseId, "A stock level must reference a warehouse.");
        }

        if (quantity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), quantity, "Quantity cannot be negative.");
        }

        if (minimumStockLevel < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumStockLevel), minimumStockLevel, "Minimum stock level cannot be negative.");
        }
    }
}
