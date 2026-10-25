using System.Net;
using System.Net.Http.Json;
using InventorySystem.Products.Application.Dtos;

namespace InventorySystem.Products.Api.Tests;

/// <summary>
/// End-to-end tests through HTTP, covering the product acceptance criteria in
/// spec section 18 (create / validate / unique SKU / retrieve / search / deactivate).
/// </summary>
public class ProductsApiTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task CreateProduct_returns_201_and_the_product_can_be_retrieved()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Sku = "sku-1001",
            Name = "Wireless Mouse",
            Description = "2.4 GHz",
            UnitPrice = 350.5m,
            MinimumStockLevel = 10
        }, Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ProductDto>(Ct);
        Assert.NotNull(created);
        Assert.Equal("SKU-1001", created!.Sku);
        Assert.Equal(350.5m, created.UnitPrice);
        Assert.True(created.IsActive);
        Assert.Equal($"/api/products/{created.Id}", response.Headers.Location?.AbsolutePath);

        var fetched = await client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}", Ct);
        Assert.Equal(created.Id, fetched!.Id);
        Assert.Equal("Wireless Mouse", fetched.Name);
    }

    [Fact]
    public async Task CreateProduct_rejects_a_duplicate_sku()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-1", Name = "Mouse", UnitPrice = 10m }, Ct);
        var duplicate = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "sku-1", Name = "Another mouse", UnitPrice = 12m }, Ct);

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains("SKU-1", await duplicate.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task CreateProduct_requires_name_and_a_non_negative_price()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var missingName = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-2", Name = "", UnitPrice = 5m }, Ct);
        var negativePrice = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-3", Name = "Mouse", UnitPrice = -1m }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, negativePrice.StatusCode);
    }

    [Fact]
    public async Task CreateProduct_rejects_an_unknown_category()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-4", Name = "Mouse", CategoryId = 999, UnitPrice = 5m }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("999", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task CreateProduct_accepts_an_existing_category()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var category = await (await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest { Name = "Electronics" }, Ct))
            .Content.ReadFromJsonAsync<CategoryDto>(Ct);

        var response = await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest
            {
                Sku = "SKU-5",
                Name = "Keyboard",
                CategoryId = category!.Id,
                UnitPrice = 1200m
            }, Ct);

        var created = await response.Content.ReadFromJsonAsync<ProductDto>(Ct);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(category.Id, created!.CategoryId);
        Assert.Equal("Electronics", created.CategoryName);
    }

    [Fact]
    public async Task Search_matches_sku_and_name_case_insensitively()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-MOUSE", Name = "Wireless Mouse", UnitPrice = 100m }, Ct);
        await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-KEY", Name = "Keyboard", UnitPrice = 200m }, Ct);

        var bySku = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=SKU-MOUSE", Ct);
        var byName = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?search=keyboard", Ct);
        var everything = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products", Ct);

        Assert.Single(bySku!.Items);
        Assert.Single(byName!.Items);
        Assert.Equal("Keyboard", byName.Items[0].Name);
        Assert.Equal(2, everything!.TotalCount);
    }

    [Fact]
    public async Task Product_list_is_paged()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        for (var i = 1; i <= 5; i++)
        {
            await client.PostAsJsonAsync("/api/products",
                new CreateProductRequest { Sku = $"SKU-{i}", Name = $"Product {i}", UnitPrice = i }, Ct);
        }

        var page = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products?page=2&pageSize=2", Ct);

        Assert.Equal(5, page!.TotalCount);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal(2, page.Page);
        Assert.Equal(3, page.TotalPages);
    }

    [Fact]
    public async Task UpdateProduct_changes_the_fields()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-6", Name = "Mouse", UnitPrice = 10m, MinimumStockLevel = 1 }, Ct))
            .Content.ReadFromJsonAsync<ProductDto>(Ct);

        var response = await client.PutAsJsonAsync($"/api/products/{created!.Id}", new UpdateProductRequest
        {
            Name = "Gaming Mouse",
            Description = "Updated",
            UnitPrice = 25.5m,
            MinimumStockLevel = 4
        }, Ct);

        var updated = await response.Content.ReadFromJsonAsync<ProductDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Gaming Mouse", updated!.Name);
        Assert.Equal(25.5m, updated.UnitPrice);
        Assert.Equal(4, updated.MinimumStockLevel);
        Assert.Equal("SKU-6", updated.Sku);
    }

    [Fact]
    public async Task UpdateProduct_returns_404_for_an_unknown_product()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsJsonAsync("/api/products/4242",
            new UpdateProductRequest { Name = "Ghost", UnitPrice = 1m }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DeleteProduct_deactivates_and_hides_it_from_the_default_list()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/products",
            new CreateProductRequest { Sku = "SKU-7", Name = "Retired", UnitPrice = 1m }, Ct))
            .Content.ReadFromJsonAsync<ProductDto>(Ct);

        var delete = await client.DeleteAsync($"/api/products/{created!.Id}", Ct);
        var stillThere = await client.GetFromJsonAsync<ProductDto>($"/api/products/{created.Id}", Ct);
        var activeOnly = await client.GetFromJsonAsync<PagedResult<ProductDto>>("/api/products", Ct);
        var includingInactive = await client.GetFromJsonAsync<PagedResult<ProductDto>>(
            "/api/products?includeInactive=true", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.False(stillThere!.IsActive);
        Assert.Empty(activeOnly!.Items);
        Assert.Single(includingInactive!.Items);
    }

    [Fact]
    public async Task GetProductById_returns_404_for_an_unknown_id()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/api/products/9999", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database_as_reachable()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body);
    }
}
