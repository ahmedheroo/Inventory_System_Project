using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Catalog;
using InventorySystem.Stock.Application.Exceptions;

namespace InventorySystem.Stock.Api.Tests.Fakes;

/// <summary>
/// Stands in for the Product service so stock operations can be tested without
/// a second running service. The real HTTP client is covered by
/// <c>CrossService.Contracts.Tests</c>.
/// </summary>
public class FakeProductCatalogClient : IProductCatalogClient
{
    private readonly Dictionary<int, ProductInfo> _products = new();
    private readonly Dictionary<int, WarehouseInfo> _warehouses = new();

    /// <summary>When true, every call fails as if the Product service were down.</summary>
    public bool Unavailable { get; set; }

    public FakeProductCatalogClient WithProduct(
        int id,
        int minimumStockLevel = 5,
        bool isActive = true)
    {
        _products[id] = new ProductInfo(id, $"SKU-{id}", $"Product {id}", 100m, minimumStockLevel, isActive);
        return this;
    }

    public FakeProductCatalogClient WithWarehouse(int id, bool isActive = true)
    {
        _warehouses[id] = new WarehouseInfo(id, $"WH-{id}", $"Warehouse {id}", isActive);
        return this;
    }

    public Task<ProductInfo?> GetProductAsync(int productId, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();

        return Task.FromResult(_products.TryGetValue(productId, out var product) ? product : null);
    }

    public Task<WarehouseInfo?> GetWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default)
    {
        EnsureAvailable();

        return Task.FromResult(_warehouses.TryGetValue(warehouseId, out var warehouse) ? warehouse : null);
    }

    private void EnsureAvailable()
    {
        if (Unavailable)
        {
            throw new CatalogUnavailableException("The Product service could not be reached.");
        }
    }
}
