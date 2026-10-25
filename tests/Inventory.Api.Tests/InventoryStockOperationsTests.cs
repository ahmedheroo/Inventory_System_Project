using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.Stock.Application.Dtos;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// The stock operations and their acceptance criteria (spec section 18):
/// product/warehouse must exist, quantity must be positive, an issue must not
/// exceed what is available, the stock level changes, a transaction is recorded
/// and an event is committed to the outbox.
/// </summary>
public class InventoryStockOperationsTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static InventoryApiFactory CreateFactory()
    {
        var factory = new InventoryApiFactory();

        factory.Catalog
            .WithProduct(1, minimumStockLevel: 10)
            .WithProduct(2, minimumStockLevel: 3)
            .WithWarehouse(1)
            .WithWarehouse(2);

        return factory;
    }

    [Fact]
    public async Task ReceiveStock_adds_stock_records_a_transaction_and_publishes_an_event()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/receive", new ReceiveStockRequest
        {
            ProductId = 1,
            WarehouseId = 1,
            Quantity = 20,
            Reference = " PO-1 "
        }, Ct);

        var inventory = await response.Content.ReadFromJsonAsync<InventoryDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(20, inventory!.Quantity);
        Assert.Equal(1, inventory.ProductId);
        Assert.Equal(1, inventory.WarehouseId);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1", Ct);

        var transaction = Assert.Single(history!.Items);
        Assert.Equal(StockTransactionType.RECEIVE, transaction.Type);
        Assert.Equal(20, transaction.Quantity);
        Assert.Equal(20, transaction.SignedQuantity);
        Assert.Equal("PO-1", transaction.Reference);

        var published = factory.SingleEvent<StockReceivedEvent>();
        Assert.Equal(1, published.ProductId);
        Assert.Equal(1, published.WarehouseId);
        Assert.Equal(20, published.Quantity);
        Assert.Equal(20, published.CurrentQuantity);
        Assert.Equal(10, published.MinimumStockLevel);
    }

    [Fact]
    public async Task ReceiveStock_accumulates_on_the_same_stock_row()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);
        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 5 }, Ct);

        var rows = await client.GetFromJsonAsync<List<InventoryDto>>("/api/inventory/1", Ct);
        var stock = await client.GetFromJsonAsync<PagedResult<InventoryDto>>("/api/inventory", Ct);

        Assert.Equal(25, Assert.Single(rows!).Quantity);
        Assert.Single(stock!.Items);
        Assert.Equal(2, factory.EnqueuedEvents().Count);
    }

    [Fact]
    public async Task IssueStock_reduces_stock_and_publishes_a_stock_issued_event()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/issue", new IssueStockRequest
        {
            ProductId = 1,
            WarehouseId = 1,
            Quantity = 5,
            Reference = "SO-77"
        }, Ct);

        var inventory = await response.Content.ReadFromJsonAsync<InventoryDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(15, inventory!.Quantity);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&type=ISSUE", Ct);

        var transaction = Assert.Single(history!.Items);
        Assert.Equal(5, transaction.Quantity);
        Assert.Equal(-5, transaction.SignedQuantity);
        Assert.Equal("SO-77", transaction.Reference);

        var published = factory.SingleEvent<StockIssuedEvent>();
        Assert.Equal(5, published.Quantity);
        Assert.Equal(15, published.CurrentQuantity);
    }

    [Fact]
    public async Task IssueStock_beyond_the_available_quantity_is_refused_and_publishes_nothing()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 10 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 11 }, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("only 10 available", body);

        var rows = await client.GetFromJsonAsync<List<InventoryDto>>("/api/inventory/1", Ct);
        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1", Ct);

        Assert.Equal(10, Assert.Single(rows!).Quantity);          // stock untouched
        Assert.Single(history!.Items);                             // only the receive
        Assert.Single(factory.EnqueuedEvents());                   // no rejected-operation event
        Assert.Empty(factory.EnqueuedEvents().OfType<StockIssuedEvent>());
    }

    [Fact]
    public async Task IssueStock_from_a_warehouse_without_stock_is_refused()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/issue",
            new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("has no stock in warehouse 1", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(factory.EnqueuedEvents());
    }

    [Fact]
    public async Task ReceiveStock_rejects_an_unknown_product_or_warehouse()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var unknownProduct = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 999, WarehouseId = 1, Quantity = 1 }, Ct);
        var unknownWarehouse = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 999, Quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, unknownProduct.StatusCode);
        Assert.Contains("Product with id '999' does not exist", await unknownProduct.Content.ReadAsStringAsync(Ct));
        Assert.Equal(HttpStatusCode.BadRequest, unknownWarehouse.StatusCode);
        Assert.Contains("Warehouse with id '999' does not exist", await unknownWarehouse.Content.ReadAsStringAsync(Ct));
        Assert.Empty(factory.EnqueuedEvents());
    }

    [Fact]
    public async Task ReceiveStock_rejects_a_deactivated_product()
    {
        using var factory = CreateFactory();
        factory.Catalog.WithProduct(77, isActive: false);
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 77, WarehouseId = 1, Quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("deactivated", await response.Content.ReadAsStringAsync(Ct));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task ReceiveStock_requires_a_positive_quantity(int quantity)
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = quantity }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(factory.EnqueuedEvents());
    }

    [Fact]
    public async Task TransferStock_moves_stock_and_records_both_legs()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/transfer", new TransferStockRequest
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 8
        }, Ct);

        var result = await response.Content.ReadFromJsonAsync<TransferResultDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(12, result!.Source.Quantity);
        Assert.Equal(8, result.Destination.Quantity);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1", Ct);

        Assert.Equal(3, history!.TotalCount);   // receive + transfer out + transfer in
        var outgoing = history.Items.Single(t => t.Type == StockTransactionType.TRANSFER_OUT);
        var incoming = history.Items.Single(t => t.Type == StockTransactionType.TRANSFER_IN);

        Assert.Equal(-8, outgoing.SignedQuantity);
        Assert.Equal(1, outgoing.WarehouseId);
        Assert.Equal(8, incoming.SignedQuantity);
        Assert.Equal(2, incoming.WarehouseId);

        var published = factory.SingleEvent<StockTransferredEvent>();
        Assert.Equal(8, published.Quantity);
        Assert.Equal(12, published.SourceCurrentQuantity);
        Assert.Equal(8, published.DestinationCurrentQuantity);
    }

    [Fact]
    public async Task TransferStock_to_the_same_warehouse_is_refused()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 10 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/transfer", new TransferStockRequest
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 1,
            Quantity = 5
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("must be different", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(factory.EnqueuedEvents().OfType<StockTransferredEvent>());
    }

    [Fact]
    public async Task TransferStock_more_than_available_is_refused_and_changes_nothing()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 5 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/transfer", new TransferStockRequest
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 6
        }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var source = await client.GetFromJsonAsync<List<InventoryDto>>("/api/inventory/1", Ct);
        var destination = await client.GetFromJsonAsync<PagedResult<InventoryDto>>(
            "/api/inventory?warehouseId=2", Ct);

        Assert.Equal(5, Assert.Single(source!).Quantity);
        Assert.Empty(destination!.Items);   // no destination row was created
        Assert.Empty(factory.EnqueuedEvents().OfType<StockTransferredEvent>());
    }

    [Fact]
    public async Task AdjustStock_sets_an_absolute_quantity_and_records_the_delta()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);

        var response = await client.PostAsJsonAsync("/api/inventory/adjust", new AdjustStockRequest
        {
            ProductId = 1,
            WarehouseId = 1,
            NewQuantity = 18,
            Reference = "Annual count"
        }, Ct);

        var inventory = await response.Content.ReadFromJsonAsync<InventoryDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(18, inventory!.Quantity);

        var history = await client.GetFromJsonAsync<PagedResult<InventoryTransactionDto>>(
            "/api/inventory/history?productId=1&type=ADJUSTMENT", Ct);

        var transaction = Assert.Single(history!.Items);
        Assert.Equal(-2, transaction.Quantity);
        Assert.Equal(-2, transaction.SignedQuantity);
        Assert.Equal("Annual count", transaction.Reference);

        var published = factory.SingleEvent<StockAdjustedEvent>();
        Assert.Equal(20, published.OldQuantity);
        Assert.Equal(18, published.NewQuantity);
    }

    [Fact]
    public async Task AdjustStock_can_create_a_stock_row_for_a_counted_quantity()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/adjust",
            new AdjustStockRequest { ProductId = 2, WarehouseId = 2, NewQuantity = 6 }, Ct);

        var inventory = await response.Content.ReadFromJsonAsync<InventoryDto>(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(6, inventory!.Quantity);

        var published = factory.SingleEvent<StockAdjustedEvent>();
        Assert.Equal(0, published.OldQuantity);
        Assert.Equal(6, published.NewQuantity);
    }

    [Fact]
    public async Task AdjustStock_rejects_a_negative_count()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/adjust",
            new AdjustStockRequest { ProductId = 1, WarehouseId = 1, NewQuantity = -1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Operations_return_503_when_the_product_service_is_unreachable()
    {
        using var factory = CreateFactory();
        factory.Catalog.Unavailable = true;
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Contains("Product service", await response.Content.ReadAsStringAsync(Ct));
        Assert.Empty(factory.EnqueuedEvents());
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database_as_reachable()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body);
    }
}
