namespace InventorySystem.BuildingBlocks.Contracts.Events;

public sealed record StockTransferredEvent : IntegrationEvent
{
    public const string Name = "stock.transferred";

    public override string EventName => Name;

    public int ProductId { get; init; }

    public int SourceWarehouseId { get; init; }

    public int DestinationWarehouseId { get; init; }

    public int Quantity { get; init; }

    public int SourceCurrentQuantity { get; init; }

    public int DestinationCurrentQuantity { get; init; }

    public int MinimumStockLevel { get; init; }
}
