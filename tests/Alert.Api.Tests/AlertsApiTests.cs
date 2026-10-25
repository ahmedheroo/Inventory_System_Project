using System.Net;
using System.Net.Http.Json;
using InventorySystem.Alerts.Application.Dtos;
using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.BuildingBlocks.Contracts.Events;

namespace InventorySystem.Alerts.Api.Tests;

/// <summary>
/// The three endpoints of the specification (section 7.3): list, get by id and
/// mark as read, including their filters, paging and error shapes.
/// </summary>
public class AlertsApiTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>Raises a low-stock alert for one (product, warehouse) pair.</summary>
    private static Task<InventorySystem.Alerts.Application.Services.AlertProcessingResult> RaiseAsync(
        AlertApiFactory factory,
        int productId,
        int warehouseId,
        int currentQuantity,
        int minimumStockLevel = 10) =>
        factory.ProcessAsync(new StockIssuedEvent
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Quantity = 1,
            CurrentQuantity = currentQuantity,
            MinimumStockLevel = minimumStockLevel
        });

    [Fact]
    public async Task List_returns_active_alerts_newest_first_and_paged()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await RaiseAsync(factory, 1, 1, 5);
        await Task.Delay(20);
        await RaiseAsync(factory, 2, 1, 0);
        await Task.Delay(20);
        await RaiseAsync(factory, 3, 2, 2);

        var all = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts", Ct);

        Assert.Equal(3, all!.TotalCount);
        Assert.Equal(new[] { 3, 2, 1 }, all.Items.Select(alert => alert.ProductId));   // newest first

        var firstPage = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?page=1&pageSize=2", Ct);
        var secondPage = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?page=2&pageSize=2", Ct);

        Assert.Equal(2, firstPage!.Items.Count);
        Assert.Single(secondPage!.Items);
        Assert.Equal(2, secondPage.Page);
        Assert.Equal(2, secondPage.TotalPages);
        Assert.True(firstPage.HasNextPage);
    }

    [Fact]
    public async Task List_filters_by_product_warehouse_type_and_read_state()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await RaiseAsync(factory, 1, 1, 5);        // low stock
        await RaiseAsync(factory, 1, 2, 0);        // out of stock
        await RaiseAsync(factory, 2, 1, 3);

        var byProduct = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?productId=1", Ct);
        var byWarehouse = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?warehouseId=2", Ct);
        var outOfStock = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?type=OUT_OF_STOCK", Ct);
        var lowStock = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?type=LOW_STOCK", Ct);

        Assert.Equal(2, byProduct!.TotalCount);
        Assert.Equal(1, Assert.Single(byWarehouse!.Items).ProductId);
        Assert.Equal(1, outOfStock!.TotalCount);
        Assert.Equal(AlertType.OUT_OF_STOCK, outOfStock.Items[0].Type);
        Assert.Equal(2, lowStock!.TotalCount);

        var alertId = lowStock.Items[0].Id;
        await client.PutAsync($"/api/alerts/{alertId}/read", null, Ct);

        var unread = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?isRead=false", Ct);
        var read = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?isRead=true", Ct);

        Assert.Equal(2, unread!.TotalCount);
        Assert.Equal(alertId, Assert.Single(read!.Items).Id);
    }

    [Fact]
    public async Task Get_by_id_returns_the_alert_and_an_unknown_id_returns_404()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await RaiseAsync(factory, 7, 3, 1, minimumStockLevel: 10);

        var listed = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts", Ct);
        var alertId = Assert.Single(listed!.Items).Id;

        var found = await client.GetAsync($"/api/alerts/{alertId}", Ct);
        var missing = await client.GetAsync("/api/alerts/424242", Ct);
        var body = await missing.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, found.StatusCode);

        var alert = await found.Content.ReadFromJsonAsync<AlertDto>(Ct);

        Assert.Equal(7, alert!.ProductId);
        Assert.Equal(3, alert.WarehouseId);
        Assert.Equal(1, alert.CurrentQuantity);
        Assert.Equal(10, alert.MinimumStockLevel);
        Assert.Equal(AlertStatus.ACTIVE, alert.Status);

        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

        // RFC 7807 shape, as the middleware promises.
        using var problem = System.Text.Json.JsonDocument.Parse(body);

        Assert.Equal(404, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("does not exist", problem.RootElement.GetProperty("detail").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task Mark_as_read_is_idempotent_and_keeps_the_first_timestamp()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await RaiseAsync(factory, 1, 1, 4);

        var alertId = Assert.Single(factory.StoredAlerts()).Id;

        var first = await client.PutAsync($"/api/alerts/{alertId}/read", null, Ct);
        var firstBody = await first.Content.ReadFromJsonAsync<AlertDto>(Ct);

        await Task.Delay(20);

        var second = await client.PutAsync($"/api/alerts/{alertId}/read", null, Ct);
        var secondBody = await second.Content.ReadFromJsonAsync<AlertDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True(firstBody!.IsRead);
        Assert.True(secondBody!.IsRead);
        Assert.NotNull(secondBody.ReadAt);
        Assert.Equal(firstBody.ReadAt, secondBody.ReadAt);
    }

    [Fact]
    public async Task Mark_as_read_returns_404_for_an_unknown_alert()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        var response = await client.PutAsync("/api/alerts/999/read", null, Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("does not exist", await response.Content.ReadAsStringAsync(Ct));
    }

    [Fact]
    public async Task An_empty_database_lists_no_alerts()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        var listed = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts", Ct);

        Assert.Empty(listed!.Items);
        Assert.Equal(0, listed.TotalCount);
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database_as_reachable()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body);
        Assert.Contains("alert", body);
    }
}
