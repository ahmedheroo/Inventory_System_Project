using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>
/// Products and warehouses by id, so a table of ids can be read as names.
///
/// The Inventory and Alert services store ids only — that is the whole point of the
/// database-per-service rule — so the console joins the ids to the catalogue in the
/// presentation layer, where a missing name costs a label instead of a cross-service
/// query.
/// </summary>
public sealed class CatalogLookup
{
    public static CatalogLookup Empty { get; } =
        new(new Dictionary<int, ProductDto>(), new Dictionary<int, WarehouseDto>());

    public CatalogLookup(IReadOnlyDictionary<int, ProductDto> products, IReadOnlyDictionary<int, WarehouseDto> warehouses)
    {
        Products = products;
        Warehouses = warehouses;
    }

    public IReadOnlyDictionary<int, ProductDto> Products { get; }

    public IReadOnlyDictionary<int, WarehouseDto> Warehouses { get; }

    public string ProductName(int productId) =>
        Products.TryGetValue(productId, out var product) ? product.Name : $"Product {productId}";

    public string Sku(int productId) =>
        Products.TryGetValue(productId, out var product) ? product.Sku : $"#{productId}";

    /// <summary>Identifiers are written in full only when the reader needs to copy them.</summary>
    public string WarehouseName(int warehouseId) =>
        Warehouses.TryGetValue(warehouseId, out var warehouse) ? warehouse.Name : $"Warehouse {warehouseId}";

    public string WarehouseCode(int warehouseId) =>
        Warehouses.TryGetValue(warehouseId, out var warehouse) ? warehouse.Code : $"#{warehouseId}";

    /// <summary>Only active warehouses are offered as operation targets.</summary>
    public IReadOnlyList<WarehouseDto> ActiveWarehouses =>
        Warehouses.Values.Where(warehouse => warehouse.IsActive).OrderBy(warehouse => warehouse.Code).ToList();

    /// <summary>Products that can take part in stock operations, ordered like the catalogue.</summary>
    public IReadOnlyList<ProductDto> ActiveProducts =>
        Products.Values.Where(product => product.IsActive).OrderBy(product => product.Sku).ToList();

    /// <summary>
    /// The level the Alert service watches for a product, or null when the console has
    /// never seen the product. The Inventory service stores quantities only, so the
    /// minimum comes from the catalogue and the comparison happens on screen.
    /// </summary>
    public int? MinimumOf(int productId) =>
        Products.TryGetValue(productId, out var product) ? product.MinimumStockLevel : null;

    public decimal UnitPriceOf(int productId) =>
        Products.TryGetValue(productId, out var product) ? product.UnitPrice : 0m;

    public string CodeOf(int productId) =>
        Products.TryGetValue(productId, out var product) ? product.Sku : $"#{productId}";
}
