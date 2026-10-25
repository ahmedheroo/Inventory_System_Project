using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;

namespace Inventory.Web.Services;

/// <summary>One catalogue read per request, shared by the pages that have to show names.</summary>
public interface ICatalogLookupService
{
    Task<CatalogLookup> GetAsync(CancellationToken cancellationToken = default);
}

public sealed class CatalogLookupService : ICatalogLookupService
{
    /// <summary>How many products are read before the console stops joining names.</summary>
    private const int MaxProducts = 1000;

    private const int PageSize = 100;

    private readonly ICatalogApi _catalog;
    private readonly ILogger<CatalogLookupService> _logger;

    private CatalogLookup? _cached;

    public CatalogLookupService(ICatalogApi catalog, ILogger<CatalogLookupService> logger)
    {
        _catalog = catalog;
        _logger = logger;
    }

    public async Task<CatalogLookup> GetAsync(CancellationToken cancellationToken = default)
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var products = new Dictionary<int, ProductDto>();
        var page = 1;
        var totalPages = 1;

        while (page <= totalPages && products.Count < MaxProducts)
        {
            var result = await _catalog.GetProductsAsync(
                new ProductQuery { IncludeInactive = true, Page = page, PageSize = PageSize },
                cancellationToken);

            foreach (var product in result.Items)
            {
                products[product.Id] = product;
            }

            totalPages = result.TotalPages;
            page++;
        }

        if (products.Count >= MaxProducts)
        {
            _logger.LogWarning(
                "The catalogue has more than {MaxProducts} products; names beyond that are shown as ids.",
                MaxProducts);
        }

        var warehouses = await _catalog.GetWarehousesAsync(includeInactive: true, cancellationToken);

        _cached = new CatalogLookup(products, warehouses.ToDictionary(warehouse => warehouse.Id));

        return _cached;
    }
}
