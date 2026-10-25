using InventorySystem.BuildingBlocks.Contracts.Events;

namespace InventorySystem.Reports.Api.Tests;

/// <summary>
/// Builds the four stock events with an explicit timestamp, so the movement
/// report's ordering and its date filter are asserted against known times rather
/// than against whatever the clock said during the test.
/// </summary>
internal static class TestEvents
{
    /// <summary>A deterministic UTC instant, <paramref name="minutes"/> after the test epoch.</summary>
    public static DateTime At(int minutes) =>
        new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes);

    public static StockReceivedEvent Received(
        int productId,
        int warehouseId,
        int quantity,
        int currentQuantity,
        DateTime occurredAt,
        int minimumStockLevel = 5) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        Quantity = quantity,
        CurrentQuantity = currentQuantity,
        MinimumStockLevel = minimumStockLevel,
        Timestamp = occurredAt
    };

    public static StockIssuedEvent Issued(
        int productId,
        int warehouseId,
        int quantity,
        int currentQuantity,
        DateTime occurredAt,
        int minimumStockLevel = 5) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        Quantity = quantity,
        CurrentQuantity = currentQuantity,
        MinimumStockLevel = minimumStockLevel,
        Timestamp = occurredAt
    };

    public static StockTransferredEvent Transferred(
        int productId,
        int sourceWarehouseId,
        int destinationWarehouseId,
        int quantity,
        int sourceCurrentQuantity,
        int destinationCurrentQuantity,
        DateTime occurredAt,
        int minimumStockLevel = 5) => new()
    {
        ProductId = productId,
        SourceWarehouseId = sourceWarehouseId,
        DestinationWarehouseId = destinationWarehouseId,
        Quantity = quantity,
        SourceCurrentQuantity = sourceCurrentQuantity,
        DestinationCurrentQuantity = destinationCurrentQuantity,
        MinimumStockLevel = minimumStockLevel,
        Timestamp = occurredAt
    };

    public static StockAdjustedEvent Adjusted(
        int productId,
        int warehouseId,
        int oldQuantity,
        int newQuantity,
        DateTime occurredAt,
        int minimumStockLevel = 5) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        OldQuantity = oldQuantity,
        NewQuantity = newQuantity,
        MinimumStockLevel = minimumStockLevel,
        Timestamp = occurredAt
    };
}
