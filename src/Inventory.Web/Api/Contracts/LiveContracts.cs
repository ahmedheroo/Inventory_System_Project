using System.Text.Json.Serialization;

namespace Inventory.Web.Api.Contracts;

public sealed record LiveStockChange(
    [property: JsonPropertyName("change")] string? Change,
    [property: JsonPropertyName("productId")] int ProductId,
    [property: JsonPropertyName("warehouseId")] int WarehouseId,
    [property: JsonPropertyName("quantity")] int Quantity,
    [property: JsonPropertyName("currentQuantity")] int CurrentQuantity,
    [property: JsonPropertyName("minimumStockLevel")] int MinimumStockLevel,
    [property: JsonPropertyName("destinationWarehouseId")] int? DestinationWarehouseId = null,
    [property: JsonPropertyName("destinationCurrentQuantity")] int? DestinationCurrentQuantity = null,
    [property: JsonPropertyName("origin")] string? Origin = null);
