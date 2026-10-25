using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Stock.Application.Dtos;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>Current-stock and history reads (TASK-025, TASK-030).</summary>
public class InventoryQueriesTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static InventoryApiFactory CreateFactory()
    {
        var factory = new InventoryApiFactory();

        factory.Catalog
            .WithProduct(1, minimumStockLevel: 10)
            .WithProduct(2, minimumStockLevel: 3)
            .WithProduct(3, minimumStockLevel: 1)
            .WithWarehouse(1)
            .WithWarehouse(2);

        return factory;
    }

    private static async Task StockAsync(HttpClient client, int productId, int warehouseId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = productId, WarehouseId = warehouseId, Quantity = quantity }, Ct);

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task GetInventory_lists_every_stock_row_and_filters_by_warehouse()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await StockAsync(client, 1, 1, 20);
        await StockAsync(client, 2, 1, 5);
        await StockAsync(client, 2, 2, 7);

        var all = await client.GetFromJsonAsync<PagedResult<InventoryDto>>("/api/inventory", Ct);
        var warehouse2 = await client.GetFromJsonAsync<PagedResult<InventoryDto>>("/api/inventory?warehouseId=2", Ct);
        var product2 = await client.GetFromJsonAsync<PagedResult<InventoryDto>>("/api/inventory?productId=2", Ct);

        Assert.Equal(3, all!.TotalCount);
        Assert.Single(warehouse2!.Items);
        Assert.Equal(7, warehouse2.Items[0].Quantity);
        Assert.Equal(2, product2!.TotalCount);
    }

    [Fact]
    public async Task GetInventory_is_paged()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await StockAsync(client, 1, 1, 20);
        await StockAsync(client, 2, 1, 5);
        await StockAsync(client, 3, 1, 2);

        var page = await client.GetFromJsonAsync<PagedResult<InventoryDto>>("/api/inventory?page=2&pageSize=2", Ct);

        Assert.Equal(3, page!.TotalCount);
        Assert.Single(page.Items);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.TotalPages);
    }

    [Fact]
    public async Task GetInventoryByProduct_returns_one_row_per_warehouse()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await StockAsync(client, 1, 1, 20);
        await client.PostAsJsonAsync("/api/inventory/transfer", new TransferStockRequest
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 6
        }, Ct);

        var rows = await client.GetFromJsonAsync<List<InventoryDto>>("/api/inventory/1", Ct);

        Assert.Equal(2, rows!.Count);
        Assert.Equal(14, rows.Single(row => row.WarehouseId == 1).Quantity);
        Assert.Equal(6, rows.Single(row => row.WarehouseId == 2).Quantity);
    }

    [Fact]
    public async Task GetInventoryByProduct_is_empty_for_an_unknown_product()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var rows = await client.GetFromJsonAsync<List<InventoryDto>>("/api/inventory/4242", Ct);

        Assert.Empty(rows!);
    }

    [Fact]
    public async Task History_is_newest_first_and_filters_by_type_and_warehouse()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await StockAsync(client, 1, 1, 20);
        await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 4, Reference = "SO-1" }, Ct);
        await client.PostAsJsonAsync("/api/inventory/transfer", new TransferStockRequest
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 3
        }, Ct);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1", Ct);
        var warehouse2Only = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&warehouseId=2", Ct);
        var issuesOnly = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&type=ISSUE", Ct);

        Assert.Equal(4, history!.TotalCount);
        Assert.Equal(StockTransactionType.RECEIVE, history.Items[^1].Type);   // oldest last
        Assert.Equal(StockTransactionType.TRANSFER_IN, history.Items[0].Type); // newest first

        Assert.Single(warehouse2Only!.Items);
        Assert.Equal("SO-1", Assert.Single(issuesOnly!.Items).Reference);
    }

    [Fact]
    public async Task History_is_paged_and_can_be_limited_by_time()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        for (var i = 0; i < 3; i++)
        {
            await StockAsync(client, 1, 1, 1);
        }

        var firstPage = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&page=1&pageSize=2", Ct);
        var secondPage = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&page=2&pageSize=2", Ct);
        var future = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            $"/api/inventory/history?productId=1&from={Uri.EscapeDataString(DateTime.UtcNow.AddDays(1).ToString("O"))}", Ct);

        Assert.Equal(3, firstPage!.TotalCount);
        Assert.Equal(2, firstPage.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Empty(future!.Items);
    }

    [Fact]
    public async Task History_is_scoped_to_the_requested_product()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await StockAsync(client, 1, 1, 5);
        await StockAsync(client, 2, 1, 5);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=2", Ct);

        Assert.Single(history!.Items);
        Assert.Equal(2, history.Items[0].ProductId);
    }
}
