using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.Catalog;
using InventorySystem.Reports.Application.Exceptions;

namespace InventorySystem.Reports.Api.Tests.Fakes;

/// <summary>
/// Stands in for the Product service so reports can be tested without a second
/// running service: the read model stores ids, the reports resolve sku, names and
/// unit prices from here. The real HTTP client is covered by its own tests.
/// </summary>
public class FakeProductCatalogClient : IProductCatalogClient
{
    private readonly Dictionary<int, ProductInfo> _products = new();
    private readonly Dictionary<int, WarehouseInfo> _warehouses = new();

    /// <summary>When true, every call fails as if the Product service were down.</summary>
    public bool Unavailable { get; set; }

    public FakeProductCatalogClient WithProduct(
        int id,
        string? sku = null,
        string? name = null,
        decimal unitPrice = 10m,
        int minimumStockLevel = 5,
        int? categoryId = null,
        bool isActive = true)
    {
        _products[id] = new ProductInfo(
            id,
            sku ?? $"SKU-{id}",
            name ?? $"Product {id}",
            categoryId,
            unitPrice,
            minimumStockLevel,
            isActive);

        return this;
    }

    public FakeProductCatalogClient WithWarehouse(
        int id,
        string? code = null,
        string? name = null,
        bool isActive = true)
    {
        _warehouses[id] = new WarehouseInfo(id, code ?? $"WH-{id}", name ?? $"Warehouse {id}", isActive);

        return this;
    }

    public Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        if (Unavailable)
        {
            throw new CatalogUnavailableException("The Product service could not be reached.");
        }

        return Task.FromResult(new CatalogSnapshot(_products, _warehouses));
    }
}
