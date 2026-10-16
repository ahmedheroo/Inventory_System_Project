namespace InventorySystem.BuildingBlocks.Contracts.Events;

public sealed record StockIssuedEvent : IntegrationEvent
{
    public const string Name = "stock.issued";

    public override string EventName => Name;

    public int ProductId { get; init; }

    public int WarehouseId { get; init; }

    public int Quantity { get; init; }

    /// <summary>Quantity on hand for (product, warehouse) after the issue.</summary>
    public int CurrentQuantity { get; init; }

    /// <summary>Minimum stock level of the product, so consumers can detect low stock without a lookup.</summary>
    public int MinimumStockLevel { get; init; }
}
