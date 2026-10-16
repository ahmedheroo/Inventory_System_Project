using InventorySystem.Stock.Application.Catalog;

namespace InventorySystem.Stock.Application.Abstractions;

/// <summary>
/// Synchronous access to catalog data owned by the Product service
/// (spec section 8: "use HTTP/REST when the caller needs an immediate response").
/// </summary>
public interface IProductCatalogClient
{
    /// <summary>Returns null when the product does not exist.</summary>
    Task<ProductInfo?> GetProductAsync(int productId, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the warehouse does not exist.</summary>
    Task<WarehouseInfo?> GetWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default);
}
