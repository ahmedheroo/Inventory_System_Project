using System.Net;
using System.Net.Http.Json;
using InventorySystem.Products.Application.Dtos;

namespace InventorySystem.Products.Api.Tests;

/// <summary>Category (TASK-006/015) and Warehouse (TASK-017 → TASK-020) API behaviour.</summary>
public class CategoriesAndWarehousesApiTests
{
    private static CancellationToken Ct => CancellationToken.None;

    [Fact]
    public async Task Category_create_list_and_update_work_end_to_end()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest { Name = "Electronics", Description = "Devices" }, Ct))
            .Content.ReadFromJsonAsync<CategoryDto>(Ct);

        Assert.Equal("Electronics", created!.Name);
        Assert.Equal(0, created.ProductCount);

        var update = await client.PutAsJsonAsync($"/api/categories/{created.Id}",
            new UpdateCategoryRequest { Name = "Consumer Electronics", Description = "Updated" }, Ct);
        var updated = await update.Content.ReadFromJsonAsync<CategoryDto>(Ct);

        var list = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Ct);

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        Assert.Equal("Consumer Electronics", updated!.Name);
        Assert.Single(list!);
    }

    [Fact]
    public async Task Category_names_must_be_unique()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/categories", new CreateCategoryRequest { Name = "Groceries" }, Ct);
        var duplicate = await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest { Name = "groceries" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Category_with_products_cannot_be_deleted()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var category = await (await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest { Name = "Stationery" }, Ct))
            .Content.ReadFromJsonAsync<CategoryDto>(Ct);

        await client.PostAsJsonAsync("/api/products", new CreateProductRequest
        {
            Sku = "SKU-9",
            Name = "A4 Paper",
            CategoryId = category!.Id,
            UnitPrice = 95m
        }, Ct);

        var delete = await client.DeleteAsync($"/api/categories/{category.Id}", Ct);

        Assert.Equal(HttpStatusCode.BadRequest, delete.StatusCode);
        Assert.Contains("products", await delete.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task Empty_category_can_be_deleted()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var category = await (await client.PostAsJsonAsync("/api/categories",
            new CreateCategoryRequest { Name = "Temporary" }, Ct))
            .Content.ReadFromJsonAsync<CategoryDto>(Ct);

        var delete = await client.DeleteAsync($"/api/categories/{category!.Id}", Ct);
        var list = await client.GetFromJsonAsync<List<CategoryDto>>("/api/categories", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(list!);
    }

    [Fact]
    public async Task Warehouse_create_update_list_and_deactivate_work_end_to_end()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var created = await (await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest { Code = "wh-cai", Name = "Cairo Main", Address = "Cairo" }, Ct))
            .Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        Assert.Equal("WH-CAI", created!.Code);
        Assert.True(created.IsActive);

        var update = await client.PutAsJsonAsync($"/api/warehouses/{created.Id}",
            new UpdateWarehouseRequest { Name = "Cairo Central", Address = "Nasr City" }, Ct);
        var updated = await update.Content.ReadFromJsonAsync<WarehouseDto>(Ct);

        Assert.Equal("Cairo Central", updated!.Name);

        var delete = await client.DeleteAsync($"/api/warehouses/{created.Id}", Ct);
        var activeOnly = await client.GetFromJsonAsync<List<WarehouseDto>>("/api/warehouses", Ct);
        var includingInactive = await client.GetFromJsonAsync<List<WarehouseDto>>(
            "/api/warehouses?includeInactive=true", Ct);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);
        Assert.Empty(activeOnly!);
        Assert.Single(includingInactive!);
        Assert.False(includingInactive![0].IsActive);
    }

    [Fact]
    public async Task Warehouse_codes_must_be_unique()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest { Code = "WH-1", Name = "First" }, Ct);
        var duplicate = await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest { Code = "wh-1", Name = "Second" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
    }

    [Fact]
    public async Task Warehouse_requires_code_and_name()
    {
        using var factory = new ProductApiFactory();
        var client = factory.CreateClient();

        var missingCode = await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest { Code = "", Name = "Cairo" }, Ct);
        var missingName = await client.PostAsJsonAsync("/api/warehouses",
            new CreateWarehouseRequest { Code = "WH-2", Name = "" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, missingCode.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, missingName.StatusCode);
    }
}
