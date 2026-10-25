using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.Catalog;
using InventorySystem.Reports.Application.Exceptions;

namespace InventorySystem.Reports.Infrastructure.Catalog;

/// <summary>
/// Reads product and warehouse data from the Product service over HTTP, so a
/// report can show sku, names and unit prices without ever touching ProductDb
/// (spec section 10).
///
/// The whole catalogue is read once per report request (a page at a time) instead
/// of one call per row, because a report row would otherwise need three round
/// trips and the reports group by product/warehouse anyway.
/// </summary>
public class HttpProductCatalogClient : IProductCatalogClient
{
    /// <summary>The Product API clamps pageSize to 100, so that is the page size to use.</summary>
    private const int PageSize = 100;

    /// <summary>A guard against an endless loop if a service ever reports endless pages.</summary>
    private const int MaxPages = 1000;

    private readonly HttpClient _httpClient;

    public HttpProductCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<CatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var products = new Dictionary<int, ProductInfo>();

        var page = 1;
        var totalPages = 1;

        while (page <= totalPages && page <= MaxPages)
        {
            var productsPage = await GetAsync<ProductsPage>(
                $"/api/products?includeInactive=true&page={page}&pageSize={PageSize}",
                cancellationToken);

            if (productsPage is null)
            {
                break;
            }

            foreach (var product in productsPage.Items)
            {
                products[product.Id] = new ProductInfo(
                    product.Id,
                    product.Sku,
                    product.Name,
                    product.CategoryId,
                    product.UnitPrice,
                    product.MinimumStockLevel,
                    product.IsActive);
            }

            totalPages = productsPage.TotalPages;
            page++;
        }

        var warehouses = await GetAsync<List<WarehouseRow>>(
            "/api/warehouses?includeInactive=true",
            cancellationToken) ?? [];

        return new CatalogSnapshot(
            products,
            warehouses.ToDictionary(
                warehouse => warehouse.Id,
                warehouse => new WarehouseInfo(warehouse.Id, warehouse.Code, warehouse.Name, warehouse.IsActive)));
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(path, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return default;
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new CatalogUnavailableException(
                    $"The Product service returned {(int)response.StatusCode} for '{path}'.");
            }

            return await response.Content.ReadFromJsonAsync<T>(cancellationToken: cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new CatalogUnavailableException(
                $"The Product service could not be reached while resolving '{path}'.", exception);
        }
        catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CatalogUnavailableException(
                $"The Product service timed out while resolving '{path}'.", exception);
        }
        catch (JsonException exception)
        {
            throw new CatalogUnavailableException(
                $"The Product service returned an unexpected payload for '{path}'.", exception);
        }
    }

    /// <summary>The shape of GET /api/products (paged).</summary>
    internal sealed class ProductsPage
    {
        public List<ProductRow> Items { get; init; } = [];

        public int TotalPages { get; init; }
    }

    internal sealed class ProductRow
    {
        public int Id { get; init; }

        public string Sku { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public int? CategoryId { get; init; }

        public decimal UnitPrice { get; init; }

        public int MinimumStockLevel { get; init; }

        public bool IsActive { get; init; }
    }

    /// <summary>The shape of GET /api/warehouses (an array).</summary>
    internal sealed class WarehouseRow
    {
        public int Id { get; init; }

        public string Code { get; init; } = string.Empty;

        public string Name { get; init; } = string.Empty;

        public bool IsActive { get; init; }
    }
}
