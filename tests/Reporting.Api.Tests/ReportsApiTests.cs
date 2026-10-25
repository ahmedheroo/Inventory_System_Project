using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Reports.Application.Dtos;

namespace InventorySystem.Reports.Api.Tests;

/// <summary>
/// The four endpoints of the specification (section 7.4) with their filters,
/// paging, summaries and error shapes. The read model is filled through the real
/// projector, so what the endpoints return is what a consumed event produced.
/// </summary>
public class ReportsApiTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>A catalogue the reports can be rendered against.</summary>
    private static void SeedCatalogue(ReportingApiFactory factory) =>
        factory.Catalog
            .WithProduct(1, "SKU-1", "Widget", unitPrice: 10m, minimumStockLevel: 5, categoryId: 1)
            .WithProduct(2, "SKU-2", "Gadget", unitPrice: 2.5m, minimumStockLevel: 1, categoryId: 2)
            .WithWarehouse(1, "WH-CAI", "Cairo Main")
            .WithWarehouse(2, "WH-ALX", "Alexandria");

    private static async Task GivenReceived(
        ReportingApiFactory factory,
        int productId,
        int warehouseId,
        int quantity,
        int minute,
        int minimumStockLevel = 5) =>
        await factory.ProjectAsync(TestEvents.Received(
            productId,
            warehouseId,
            quantity,
            quantity,
            TestEvents.At(minute),
            minimumStockLevel));

    /// <summary>
    /// Issues stock down to <paramref name="currentQuantity"/> — how a pair reaches
    /// zero in real life (an issue of zero units is not an operation).
    /// </summary>
    private static async Task GivenIssued(
        ReportingApiFactory factory,
        int productId,
        int warehouseId,
        int quantity,
        int currentQuantity,
        int minute,
        int minimumStockLevel = 5) =>
        await factory.ProjectAsync(TestEvents.Issued(
            productId,
            warehouseId,
            quantity,
            currentQuantity,
            TestEvents.At(minute),
            minimumStockLevel));

    // -----------------------------------------------------------------------
    // GET /api/reports/inventory
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_inventory_report_returns_enriched_rows_and_summary_totals()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 10, 0);
        await GivenReceived(factory, 2, 2, 4, 1, minimumStockLevel: 1);

        var report = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory", Ct);

        Assert.Equal(2, report!.TotalCount);
        Assert.Equal(2, report.Items.Count);

        var widget = report.Items.Single(row => row.ProductId == 1);

        Assert.Equal("SKU-1", widget.Sku);
        Assert.Equal("Widget", widget.ProductName);
        Assert.Equal("WH-CAI", widget.WarehouseCode);
        Assert.Equal("Cairo Main", widget.WarehouseName);
        Assert.Equal(10, widget.Quantity);
        Assert.Equal(5, widget.MinimumStockLevel);
        Assert.Equal(10m, widget.UnitPrice);
        Assert.Equal(100m, widget.Value);
        Assert.False(widget.IsLowStock);
        Assert.False(widget.IsOutOfStock);

        var gadget = report.Items.Single(row => row.ProductId == 2);

        Assert.Equal(2.5m, gadget.UnitPrice);
        Assert.Equal(10m, gadget.Value);                 // 4 × 2.5

        Assert.Equal(2, report.Summary.ProductCount);
        Assert.Equal(2, report.Summary.WarehouseCount);
        Assert.Equal(2, report.Summary.StockLevelCount);
        Assert.Equal(14, report.Summary.TotalQuantity);
        Assert.Equal(110m, report.Summary.TotalValue);
        Assert.Equal(0, report.Summary.LowStockCount);
        Assert.Equal(0, report.Summary.OutOfStockCount);
    }

    [Fact]
    public async Task The_inventory_report_pages_and_filters()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 10, 0);
        await GivenReceived(factory, 2, 2, 4, 1, minimumStockLevel: 1);

        var firstPage = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory?page=1&pageSize=1", Ct);
        var secondPage = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory?page=2&pageSize=1", Ct);

        Assert.Single(firstPage!.Items);
        Assert.Single(secondPage!.Items);
        Assert.Equal(2, firstPage.TotalPages);
        Assert.Equal(2, firstPage.TotalCount);                  // the total is the filtered set, not the page
        Assert.True(firstPage.HasNextPage);
        Assert.False(firstPage.HasPreviousPage);
        Assert.True(secondPage.HasPreviousPage);

        var byProduct = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory?productId=1", Ct);
        var byWarehouse = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory?warehouseId=2", Ct);

        Assert.Equal(1, byProduct!.TotalCount);
        Assert.Equal(10, byProduct.Summary.TotalQuantity);
        Assert.Equal(1, byWarehouse!.TotalCount);
        Assert.Equal(2, byWarehouse.Items.Single().ProductId);
    }

    [Fact]
    public async Task The_inventory_report_carries_the_low_stock_flags()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 3, 0);                        // 3 <= 5
        await GivenIssued(factory, 1, 2, quantity: 3, currentQuantity: 0, minute: 1);

        var report = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory", Ct);

        Assert.Equal(2, report!.Summary.LowStockCount);
        Assert.Equal(1, report.Summary.OutOfStockCount);
        Assert.Equal(3, report.Summary.TotalQuantity);   // the zero row adds nothing
        Assert.Equal(30m, report.Summary.TotalValue);    // 3 × 10
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/low-stock
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_low_stock_report_applies_the_minimum_rule_and_reports_shortfalls()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 3, 0);                        // low, shortfall 2
        await GivenIssued(factory, 1, 2, quantity: 3, currentQuantity: 0, minute: 1);   // out of stock, shortfall 5
        await GivenReceived(factory, 2, 1, 7, 2, minimumStockLevel: 1);  // healthy, not reported

        var report = await client.GetFromJsonAsync<LowStockReport>("/api/reports/low-stock", Ct);

        Assert.Equal(2, report!.TotalCount);

        // Worst first: nothing in stock, then the largest shortfall.
        Assert.Equal(LowStockStatus.OUT_OF_STOCK, report.Items[0].Status);
        Assert.Equal(2, report.Items[0].WarehouseId);
        Assert.Equal(5, report.Items[0].Shortfall);
        Assert.Equal("SKU-1", report.Items[0].Sku);
        Assert.Equal("Alexandria", report.Items[0].WarehouseName);

        Assert.Equal(LowStockStatus.LOW_STOCK, report.Items[1].Status);
        Assert.Equal(3, report.Items[1].Quantity);
        Assert.Equal(2, report.Items[1].Shortfall);

        Assert.Equal(1, report.Summary.LowStockCount);
        Assert.Equal(1, report.Summary.OutOfStockCount);
        Assert.Equal(7, report.Summary.TotalShortfall);
        Assert.Equal(1, report.Summary.AffectedProducts);
        Assert.Equal(2, report.Summary.AffectedWarehouses);
    }

    [Fact]
    public async Task The_low_stock_report_filters_by_warehouse_and_can_exclude_out_of_stock_rows()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 3, 0);
        await GivenIssued(factory, 1, 2, quantity: 3, currentQuantity: 0, minute: 1);

        var byWarehouse = await client.GetFromJsonAsync<LowStockReport>("/api/reports/low-stock?warehouseId=2", Ct);
        var existingOnly = await client.GetFromJsonAsync<LowStockReport>(
            "/api/reports/low-stock?includeOutOfStock=false",
            Ct);

        Assert.Equal(1, byWarehouse!.TotalCount);
        Assert.Equal(LowStockStatus.OUT_OF_STOCK, byWarehouse.Items.Single().Status);

        Assert.Equal(1, existingOnly!.TotalCount);
        Assert.Equal(LowStockStatus.LOW_STOCK, existingOnly.Items.Single().Status);
        Assert.Equal(0, existingOnly.Summary.OutOfStockCount);
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/movement
    // -----------------------------------------------------------------------

    /// <summary>Receive, issue, transfer and adjust one product, each one minute apart.</summary>
    private static async Task SeedMovementsAsync(ReportingApiFactory factory)
    {
        await GivenReceived(factory, 1, 1, 10, 0);
        await factory.ProjectAsync(TestEvents.Issued(1, 1, 4, 6, TestEvents.At(1)));
        await factory.ProjectAsync(TestEvents.Transferred(
            1,
            sourceWarehouseId: 1,
            destinationWarehouseId: 2,
            quantity: 2,
            sourceCurrentQuantity: 4,
            destinationCurrentQuantity: 2,
            TestEvents.At(2)));
        await factory.ProjectAsync(TestEvents.Adjusted(1, 1, oldQuantity: 4, newQuantity: 3, TestEvents.At(3)));
    }

    [Fact]
    public async Task The_movement_report_lists_the_ledger_newest_first_with_a_summary()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await SeedMovementsAsync(factory);

        var report = await client.GetFromJsonAsync<MovementReport>("/api/reports/movement", Ct);

        Assert.Equal(5, report!.TotalCount);

        Assert.Equal(
            new[]
            {
                StockTransactionType.ADJUSTMENT,
                StockTransactionType.TRANSFER_IN,
                StockTransactionType.TRANSFER_OUT,
                StockTransactionType.ISSUE,
                StockTransactionType.RECEIVE
            },
            report.Items.Select(row => row.Type));

        var adjustment = report.Items[0];

        Assert.Equal(-1, adjustment.Quantity);
        Assert.Equal(-1, adjustment.SignedQuantity);
        Assert.Equal(3, adjustment.QuantityAfter);
        Assert.Equal(TestEvents.At(3), adjustment.OccurredAt);
        Assert.Equal("Widget", adjustment.ProductName);
        Assert.Equal("WH-CAI", adjustment.WarehouseCode);

        Assert.Equal(5, report.Summary.TotalMovements);
        Assert.Equal(10, report.Summary.TotalReceived);
        Assert.Equal(4, report.Summary.TotalIssued);
        Assert.Equal(4, report.Summary.TotalTransferred);     // 2 out + 2 in
        Assert.Equal(0, report.Summary.TotalAdjustedUp);
        Assert.Equal(1, report.Summary.TotalAdjustedDown);
        Assert.Equal(5, report.Summary.NetChange);            // +10 +2 -4 -2 -1
    }

    [Fact]
    public async Task The_movement_report_filters_by_type_product_and_warehouse()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await SeedMovementsAsync(factory);
        await GivenReceived(factory, 2, 1, 5, 4);

        var issues = await client.GetFromJsonAsync<MovementReport>("/api/reports/movement?type=ISSUE", Ct);
        var destination = await client.GetFromJsonAsync<MovementReport>("/api/reports/movement?warehouseId=2", Ct);
        var product2 = await client.GetFromJsonAsync<MovementReport>("/api/reports/movement?productId=2", Ct);

        Assert.Equal(StockTransactionType.ISSUE, issues!.Items.Single().Type);
        Assert.Equal(StockTransactionType.TRANSFER_IN, destination!.Items.Single().Type);
        Assert.Equal(1, product2!.TotalCount);
        Assert.Equal(2, product2.Items.Single().ProductId);
    }

    [Fact]
    public async Task The_movement_report_filters_by_date_range_inclusively()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 10, 0);
        await factory.ProjectAsync(TestEvents.Issued(1, 1, 4, 6, TestEvents.At(1)));
        await factory.ProjectAsync(TestEvents.Adjusted(1, 1, oldQuantity: 6, newQuantity: 5, TestEvents.At(2)));

        var boundary = Uri.EscapeDataString(TestEvents.At(1).ToString("O"));
        var upper = Uri.EscapeDataString(TestEvents.At(2).ToString("O"));

        var atOne = await client.GetFromJsonAsync<MovementReport>($"/api/reports/movement?from={boundary}&to={boundary}", Ct);
        var upToTwo = await client.GetFromJsonAsync<MovementReport>($"/api/reports/movement?to={upper}", Ct);
        var pagedFromOne = await client.GetFromJsonAsync<MovementReport>($"/api/reports/movement?from={boundary}&pageSize=1", Ct);

        Assert.Equal(1, atOne!.TotalCount);                       // both bounds are inclusive
        Assert.Equal(StockTransactionType.ISSUE, atOne.Items.Single().Type);

        Assert.Equal(3, upToTwo!.TotalCount);
        Assert.Equal(2, pagedFromOne!.TotalCount);                 // the newest two
        Assert.Single(pagedFromOne.Items);
        Assert.Equal(2, pagedFromOne.Summary.TotalMovements);      // the summary covers the filter, not only the page
    }

    [Fact]
    public async Task The_movement_report_rejects_a_reversed_date_range()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        var from = Uri.EscapeDataString(TestEvents.At(3).ToString("O"));
        var to = Uri.EscapeDataString(TestEvents.At(1).ToString("O"));

        var response = await client.GetAsync($"/api/reports/movement?from={from}&to={to}", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var problem = System.Text.Json.JsonDocument.Parse(body);

        Assert.Equal(400, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Contains("'from'", problem.RootElement.GetProperty("detail").GetString());
        Assert.True(problem.RootElement.TryGetProperty("traceId", out _));
    }

    // -----------------------------------------------------------------------
    // GET /api/reports/valuation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task The_valuation_report_multiplies_quantity_by_unit_price_and_groups_by_warehouse()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 5, 0);     // 5 × 10 = 50
        await GivenReceived(factory, 1, 2, 3, 1);     // 3 × 10 = 30
        await GivenReceived(factory, 2, 2, 4, 2);     // 4 × 2.5 = 10

        var report = await client.GetFromJsonAsync<ValuationReport>("/api/reports/valuation", Ct);

        Assert.Equal(3, report!.TotalCount);
        Assert.Equal(new[] { 50m, 30m, 10m }, report.Items.Select(row => row.Value));

        Assert.Equal(1, report.Items[0].CategoryId);
        Assert.Equal(12, report.Summary.TotalQuantity);
        Assert.Equal(90m, report.Summary.TotalValue);
        Assert.Equal(2, report.Summary.ProductCount);
        Assert.Equal(2, report.Summary.WarehouseCount);

        Assert.Equal(2, report.ByWarehouse.Count);
        Assert.Equal("WH-CAI", report.ByWarehouse[0].WarehouseCode);
        Assert.Equal(5, report.ByWarehouse[0].Quantity);
        Assert.Equal(50m, report.ByWarehouse[0].Value);
        Assert.Equal(40m, report.ByWarehouse[1].Value);           // Alexandria: 30 + 10
    }

    [Fact]
    public async Task The_valuation_report_filters_by_warehouse_and_category()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 5, 0);
        await GivenReceived(factory, 1, 2, 3, 1);
        await GivenReceived(factory, 2, 2, 4, 2);

        var byWarehouse = await client.GetFromJsonAsync<ValuationReport>("/api/reports/valuation?warehouseId=2", Ct);
        var byCategory = await client.GetFromJsonAsync<ValuationReport>("/api/reports/valuation?categoryId=1", Ct);
        var unknownCategory = await client.GetFromJsonAsync<ValuationReport>("/api/reports/valuation?categoryId=99", Ct);

        Assert.Equal(2, byWarehouse!.TotalCount);
        Assert.Equal(40m, byWarehouse.Summary.TotalValue);
        Assert.Single(byWarehouse.ByWarehouse);

        Assert.Equal(2, byCategory!.TotalCount);                   // only "Widget" (category 1)
        Assert.Equal(80m, byCategory.Summary.TotalValue);

        Assert.Equal(0, unknownCategory!.TotalCount);
        Assert.Empty(unknownCategory.ByWarehouse);
    }

    // -----------------------------------------------------------------------
    // Failure and edge cases
    // -----------------------------------------------------------------------

    [Fact]
    public async Task A_report_returns_503_when_the_product_service_is_unreachable()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        await GivenReceived(factory, 1, 1, 10, 0);

        factory.Catalog.Unavailable = true;

        var response = await client.GetAsync("/api/reports/inventory", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Product service", body);
        Assert.Contains("Dependency unavailable", body);
    }

    [Fact]
    public async Task An_empty_read_model_produces_empty_reports_with_zeroed_summaries()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        var inventory = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory", Ct);
        var lowStock = await client.GetFromJsonAsync<LowStockReport>("/api/reports/low-stock", Ct);
        var movement = await client.GetFromJsonAsync<MovementReport>("/api/reports/movement", Ct);
        var valuation = await client.GetFromJsonAsync<ValuationReport>("/api/reports/valuation", Ct);

        Assert.Empty(inventory!.Items);
        Assert.Equal(0, inventory.Summary.TotalValue);
        Assert.Equal(0, inventory.TotalPages);

        Assert.Empty(lowStock!.Items);
        Assert.Equal(0, lowStock.Summary.TotalShortfall);

        Assert.Empty(movement!.Items);
        Assert.Equal(0, movement.Summary.TotalMovements);
        Assert.Equal(0, movement.Summary.NetChange);

        Assert.Empty(valuation!.Items);
        Assert.Empty(valuation.ByWarehouse);
        Assert.Equal(0m, valuation.Summary.TotalValue);
    }

    [Fact]
    public async Task An_unknown_product_id_is_still_reported_with_a_placeholder_label()
    {
        using var factory = new ReportingApiFactory();
        SeedCatalogue(factory);
        var client = factory.CreateClient();

        // Nothing in the catalogue knows product 99: the row must still appear.
        await GivenReceived(factory, 99, 1, 2, 0);

        var report = await client.GetFromJsonAsync<InventoryReport>("/api/reports/inventory", Ct);

        var row = Assert.Single(report!.Items);

        Assert.Equal("#99", row.Sku);
        Assert.Equal("Unknown product 99", row.ProductName);
        Assert.Equal(0m, row.Value);                                // no price to value it with
        Assert.Equal("Cairo Main", row.WarehouseName);              // the warehouse is known
    }

    [Fact]
    public async Task Health_endpoint_reports_the_database_as_reachable()
    {
        using var factory = new ReportingApiFactory();
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health", Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("healthy", body);
        Assert.Contains("reporting", body);
    }
}
