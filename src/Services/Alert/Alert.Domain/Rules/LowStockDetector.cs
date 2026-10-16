using InventorySystem.Alerts.Domain.Entities;

namespace InventorySystem.Alerts.Domain.Rules;

/// <summary>
/// The low-stock rule from the specification (section 7.3):
/// <c>CurrentQuantity &lt;= MinimumStockLevel</c> raises an alert. Reviewed here,
/// in one place, so the API, the consumer and the tests all use the same rule.
/// </summary>
public static class LowStockDetector
{
    // Product and warehouse names are not part of the event payload (they belong
    // to the Product service), so alerts are identified by id and enriched by the
    // MVC layer that already knows the catalogue.
    public const int MessageMaxLength = Alert.MessageMaxLength;

    /// <summary>
    /// Returns the alert type the observed quantity deserves, or <c>null</c> when
    /// there is no alert to raise (or one to resolve).
    /// </summary>
    public static AlertType? Detect(int currentQuantity, int minimumStockLevel) =>
        currentQuantity > minimumStockLevel
            ? null
            : currentQuantity == 0
                ? AlertType.OUT_OF_STOCK
                : AlertType.LOW_STOCK;

    public static bool IsLowStock(int currentQuantity, int minimumStockLevel) =>
        Detect(currentQuantity, minimumStockLevel) is not null;

    /// <summary>Human-readable alert text, kept short enough for the entity limit.</summary>
    public static string Describe(
        int productId,
        int warehouseId,
        AlertType type,
        int currentQuantity,
        int minimumStockLevel)
    {
        var message = type == AlertType.OUT_OF_STOCK
            ? $"Product {productId} is out of stock in warehouse {warehouseId} (minimum stock level {minimumStockLevel})."
            : $"Product {productId} is low on stock in warehouse {warehouseId}: {currentQuantity} left (minimum stock level {minimumStockLevel}).";

        return message.Length <= MessageMaxLength ? message : message[..MessageMaxLength];
    }
}
