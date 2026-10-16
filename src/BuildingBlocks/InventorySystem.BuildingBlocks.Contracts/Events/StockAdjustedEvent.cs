namespace InventorySystem.BuildingBlocks.Contracts.Events;

public sealed record StockAdjustedEvent : IntegrationEvent
{
    public const string Name = "stock.adjusted";

    public override string EventName => Name;

    public int ProductId { get; init; }

    public int WarehouseId { get; init; }

    public int OldQuantity { get; init; }

    public int NewQuantity { get; init; }

    public int MinimumStockLevel { get; init; }
}
