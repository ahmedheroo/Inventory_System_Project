namespace InventorySystem.BuildingBlocks.Contracts.Events;

public sealed record StockReceivedEvent : IntegrationEvent
{
    public const string Name = "stock.received";

    public override string EventName => Name;

    public int ProductId { get; init; }

    public int WarehouseId { get; init; }

    public int Quantity { get; init; }

    /// <summary>Quantity on hand for (product, warehouse) after the operation.</summary>
    public int CurrentQuantity { get; init; }

    /// <summary>Minimum stock level of the product, so consumers can detect low stock without a lookup.</summary>
    public int MinimumStockLevel { get; init; }
}
