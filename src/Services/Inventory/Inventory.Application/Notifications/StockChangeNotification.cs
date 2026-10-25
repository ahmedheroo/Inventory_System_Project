using System.Text.Json.Serialization;

namespace InventorySystem.Stock.Application.Notifications;

/// <summary>Which operation changed a stock level. One case per published event.</summary>
public enum StockChangeKind
{
    RECEIVE = 0,
    ISSUE = 1,
    TRANSFER = 2,
    ADJUSTMENT = 3
}

/// <summary>
/// What a live subscriber is told after a stock change has been committed.
///
/// This is deliberately not the integration event: a screen needs the movement and the
/// state it left behind, not the payload the Alert and Reporting services project. It is
/// also deliberately small — a browser holds it in memory for as long as the page is open.
/// </summary>
/// <param name="Change">The operation that moved the stock.</param>
/// <param name="ProductId">The product that moved.</param>
/// <param name="WarehouseId">The warehouse it moved in — the source, for a transfer.</param>
/// <param name="Quantity">The magnitude of the movement.</param>
/// <param name="CurrentQuantity">The quantity the operation left in <paramref name="WarehouseId"/>.</param>
/// <param name="MinimumStockLevel">The product's minimum at the moment of the operation, so a screen can read the state without another lookup.</param>
/// <param name="DestinationWarehouseId">Where a transfer arrived, or null for the other three operations.</param>
/// <param name="DestinationCurrentQuantity">The quantity a transfer left in the destination, or null.</param>
/// <param name="Origin">
/// The label of the terminal that asked for the operation, when it named one. It is echoed, never
/// interpreted: it lets a watching screen tell its own movements from everybody else's, and it
/// carries no authority. Null means the caller did not identify itself.
/// </param>
public sealed record StockChangeNotification(
    [property: JsonConverter(typeof(JsonStringEnumConverter))] StockChangeKind Change,
    int ProductId,
    int WarehouseId,
    int Quantity,
    int CurrentQuantity,
    int MinimumStockLevel,
    int? DestinationWarehouseId = null,
    int? DestinationCurrentQuantity = null,
    string? Origin = null);
