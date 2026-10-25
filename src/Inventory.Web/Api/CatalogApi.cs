using System.Net.Http.Json;
using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Api;

/// The catalogue, as the console sees it: products, categories and warehouses,
/// all owned by the Product service.

public interface ICatalogApi
{
    Task<PagedResult<ProductDto>> GetProductsAsync(ProductQuery query, CancellationToken cancellationToken = default);

    Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default);

    Task<ProductDto> CreateProductAsync(CreateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto> UpdateProductAsync(int id, UpdateProductRequest request, CancellationToken cancellationToken = default);

    Task<ProductDto> ChangeSkuAsync(int id, string sku, CancellationToken cancellationToken = default);

    Task DeactivateProductAsync(int id, CancellationToken cancellationToken = default);

    Task<ProductDto> ActivateProductAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken cancellationToken = default);

    Task<CategoryDto> UpdateCategoryAsync(int id, UpdateCategoryRequest request, CancellationToken cancellationToken = default);

    Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(bool includeInactive = false, CancellationToken cancellationToken = default);

    Task<WarehouseDto?> GetWarehouseAsync(int id, CancellationToken cancellationToken = default);

    Task<WarehouseDto> CreateWarehouseAsync(CreateWarehouseRequest request, CancellationToken cancellationToken = default);

    Task<WarehouseDto> UpdateWarehouseAsync(int id, UpdateWarehouseRequest request, CancellationToken cancellationToken = default);

    Task DeactivateWarehouseAsync(int id, CancellationToken cancellationToken = default);

    Task<WarehouseDto> ActivateWarehouseAsync(int id, CancellationToken cancellationToken = default);
}

public sealed class CatalogApi : ApiClientBase, ICatalogApi
{
    public CatalogApi(HttpClient httpClient, ILogger<CatalogApi> logger)
        : base(httpClient, logger, "Product service")
    {
    }

    public Task<PagedResult<ProductDto>> GetProductsAsync(
        ProductQuery query,
        CancellationToken cancellationToken = default) =>
        GetAsync<PagedResult<ProductDto>>(
            "/api/products" + ApiQuery.Build(
                ("search", query.Search),
                ("categoryId", query.CategoryId),
                ("includeInactive", query.IncludeInactive),
                ("page", query.Page),
                ("pageSize", query.PageSize)),
            cancellationToken);

    public Task<ProductDto?> GetProductAsync(int id, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<ProductDto>($"/api/products/{id}", cancellationToken);

    public Task<ProductDto> CreateProductAsync(
        CreateProductRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<ProductDto>("/api/products", request, cancellationToken);

    public Task<ProductDto> UpdateProductAsync(
        int id,
        UpdateProductRequest request,
        CancellationToken cancellationToken = default) =>
        PutAsync<ProductDto>($"/api/products/{id}", request, cancellationToken);

    public Task<ProductDto> ChangeSkuAsync(int id, string sku, CancellationToken cancellationToken = default) =>
        PutAsync<ProductDto>($"/api/products/{id}/sku", new { sku }, cancellationToken);

    public Task DeactivateProductAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"/api/products/{id}", body: null, cancellationToken);

    public Task<ProductDto> ActivateProductAsync(int id, CancellationToken cancellationToken = default) =>
        PostAsync<ProductDto>($"/api/products/{id}/activate", body: null, cancellationToken);

    public Task<IReadOnlyList<CategoryDto>> GetCategoriesAsync(CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<CategoryDto>>("/api/categories", cancellationToken);

    public Task<CategoryDto> CreateCategoryAsync(
        CreateCategoryRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<CategoryDto>("/api/categories", request, cancellationToken);

    public Task<CategoryDto> UpdateCategoryAsync(
        int id,
        UpdateCategoryRequest request,
        CancellationToken cancellationToken = default) =>
        PutAsync<CategoryDto>($"/api/categories/{id}", request, cancellationToken);

    public Task DeleteCategoryAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"/api/categories/{id}", body: null, cancellationToken);

    public Task<IReadOnlyList<WarehouseDto>> GetWarehousesAsync(
        bool includeInactive = false,
        CancellationToken cancellationToken = default) =>
        GetAsync<IReadOnlyList<WarehouseDto>>(
            "/api/warehouses" + ApiQuery.Build(("includeInactive", includeInactive)),
            cancellationToken);

    public Task<WarehouseDto?> GetWarehouseAsync(int id, CancellationToken cancellationToken = default) =>
        GetOrNullAsync<WarehouseDto>($"/api/warehouses/{id}", cancellationToken);

    public Task<WarehouseDto> CreateWarehouseAsync(
        CreateWarehouseRequest request,
        CancellationToken cancellationToken = default) =>
        PostAsync<WarehouseDto>("/api/warehouses", request, cancellationToken);

    public Task<WarehouseDto> UpdateWarehouseAsync(
        int id,
        UpdateWarehouseRequest request,
        CancellationToken cancellationToken = default) =>
        PutAsync<WarehouseDto>($"/api/warehouses/{id}", request, cancellationToken);

    public Task DeactivateWarehouseAsync(int id, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Delete, $"/api/warehouses/{id}", body: null, cancellationToken);

    public Task<WarehouseDto> ActivateWarehouseAsync(int id, CancellationToken cancellationToken = default) =>
        PostAsync<WarehouseDto>($"/api/warehouses/{id}/activate", body: null, cancellationToken);
}
