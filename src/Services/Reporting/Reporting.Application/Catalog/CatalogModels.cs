namespace InventorySystem.Reports.Application.Catalog;

/// <summary>
/// The subset of Product service data the reports need in order to render a row:
/// a movement or stock level carries ids, the catalogue turns them into
/// sku / name / unit price. Reporting never touches ProductDb (spec section 10).
/// </summary>
public sealed record ProductInfo(
    int Id,
    string Sku,
    string Name,
    int? CategoryId,
    decimal UnitPrice,
    int MinimumStockLevel,
    bool IsActive);

/// <summary>Warehouse details owned by the Product service.</summary>
public sealed record WarehouseInfo(
    int Id,
    string Code,
    string Name,
    bool IsActive);

/// <summary>
/// The product and warehouse lookups for one report request, read once so a
/// report does not issue a call per row.
/// </summary>
public sealed class CatalogSnapshot
{
    public static CatalogSnapshot Empty { get; } = new(
        new Dictionary<int, ProductInfo>(),
        new Dictionary<int, WarehouseInfo>());

    public CatalogSnapshot(
        IReadOnlyDictionary<int, ProductInfo> products,
        IReadOnlyDictionary<int, WarehouseInfo> warehouses)
    {
        Products = products;
        Warehouses = warehouses;
    }

    public IReadOnlyDictionary<int, ProductInfo> Products { get; }

    public IReadOnlyDictionary<int, WarehouseInfo> Warehouses { get; }

    /// <summary>The product, or null when the catalogue does not know that id.</summary>
    public ProductInfo? Product(int productId) =>
        Products.TryGetValue(productId, out var product) ? product : null;

    /// <summary>The warehouse, or null when the catalogue does not know that id.</summary>
    public WarehouseInfo? Warehouse(int warehouseId) =>
        Warehouses.TryGetValue(warehouseId, out var warehouse) ? warehouse : null;
}
