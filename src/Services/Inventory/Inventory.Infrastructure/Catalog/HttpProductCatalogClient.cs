using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Catalog;
using InventorySystem.Stock.Application.Exceptions;

namespace InventorySystem.Stock.Infrastructure.Catalog;

/// <summary>
/// Reads product and warehouse data from the Product service over HTTP.
/// Registered as a typed <see cref="HttpClient"/> so the base address, timeout
/// and connection pooling are managed by the framework.
/// </summary>
public class HttpProductCatalogClient : IProductCatalogClient
{
    private readonly HttpClient _httpClient;

    public HttpProductCatalogClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public Task<ProductInfo?> GetProductAsync(int productId, CancellationToken cancellationToken = default) =>
        GetAsync<ProductInfo>($"/api/products/{productId}", cancellationToken);

    public Task<WarehouseInfo?> GetWarehouseAsync(int warehouseId, CancellationToken cancellationToken = default) =>
        GetAsync<WarehouseInfo>($"/api/warehouses/{warehouseId}", cancellationToken);

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
}
