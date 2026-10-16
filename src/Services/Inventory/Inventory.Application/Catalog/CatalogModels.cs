namespace InventorySystem.Stock.Application.Catalog;


/// The subset of Product service data the Inventory service depends on.
/// Read over HTTP — the Inventory service never touches ProductDb (spec section 10).

public sealed record ProductInfo(
    int Id,
    string Sku,
    string Name,
    decimal UnitPrice,
    int MinimumStockLevel,
    bool IsActive);

/// Warehouse details owned by the Product service
public sealed record WarehouseInfo(
    int Id,
    string Code,
    string Name,
    bool IsActive);
