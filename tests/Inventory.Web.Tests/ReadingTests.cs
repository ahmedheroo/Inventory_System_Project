using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// How the console turns a page into a link and a filter into a call. These are the
/// rules a reader never sees failing until a CSV disagrees with the table above it.
/// </summary>
public class ReadingTests
{
    [Fact]
    public void The_pager_keeps_every_filter_and_only_moves_the_page()
    {
        var pager = new Pager(
            "/products",
            new Dictionary<string, string?>
            {
                ["search"] = "widget",
                ["categoryId"] = "2",
                ["includeInactive"] = null,
                ["pageSize"] = "50"
            },
            page: 2,
            pageSize: 50,
            totalCount: 240);

        Assert.Equal("/products?search=widget&categoryId=2&pageSize=50&page=3", pager.Url(3));
        Assert.Equal(5, pager.TotalPages);
        Assert.Equal(51, pager.FirstRowNumber);
        Assert.Equal(100, pager.LastRowNumber);
        Assert.True(pager.HasNextPage);
        Assert.True(pager.HasPreviousPage);
    }

    [Fact]
    public void The_pager_clamps_a_page_past_the_end_and_an_empty_result_says_nothing()
    {
        var past = new Pager("/inventory", new Dictionary<string, string?>(), page: 9, pageSize: 20, totalCount: 25);
        var empty = new Pager("/inventory", new Dictionary<string, string?>(), page: 1, pageSize: 20, totalCount: 0);

        Assert.Equal(2, past.Page);
        Assert.False(past.HasNextPage);
        Assert.Equal(0, empty.TotalPages);
        Assert.Equal(0, empty.FirstRowNumber);
    }

    [Fact]
    public void A_report_download_carries_the_filters_and_never_the_paging()
    {
        var filters = new ReportFilters
        {
            ProductId = 7,
            WarehouseId = 2,
            Type = StockTransactionType.ISSUE,
            From = new DateTime(2026, 10, 1),
            To = new DateTime(2026, 10, 16),
            Page = 4,
            PageSize = 50
        };

        var query = filters.ToQuery();

        Assert.Equal("7", query["productId"]);
        Assert.Equal("2", query["warehouseId"]);
        Assert.Equal("ISSUE", query["type"]);
        Assert.Equal("2026-10-01", query["from"]);
        Assert.Equal("2026-10-16", query["to"]);
        Assert.Equal("4", query["page"]);

        Assert.Equal(
            "/reports/movement/export?productId=7&warehouseId=2&type=ISSUE&from=2026-10-01&to=2026-10-16&format=xlsx",
            filters.DownloadUrl("movement", "xlsx"));
    }

    [Fact]
    public void Excluding_out_of_stock_travels_because_the_service_defaults_to_including_it()
    {
        var included = new ReportFilters { IncludeOutOfStock = true };
        var excluded = new ReportFilters { IncludeOutOfStock = false };

        Assert.False(included.ToQuery().ContainsKey("includeOutOfStock"));
        Assert.Equal("false", excluded.ToQuery()["includeOutOfStock"]);
    }

    [Fact]
    public void A_filter_line_reads_back_what_is_in_force()
    {
        var filters = new ReportFilters
        {
            ProductId = 3,
            WarehouseId = 1,
            Type = StockTransactionType.ADJUSTMENT,
            From = new DateTime(2026, 10, 1),
            IncludeOutOfStock = false
        };

        var catalogue = new CatalogLookup(
            new Dictionary<int, ProductDto> { [3] = Product(3, "SKU-3", "Widget") },
            new Dictionary<int, WarehouseDto> { [1] = Warehouse(1, "CAI", "Cairo Main") });

        var described = filters.Describe(catalogue);

        Assert.Contains("product SKU-3", described);
        Assert.Contains("warehouse Cairo Main", described);
        Assert.Contains("adjusted movements", described);
        Assert.Contains("from 2026-10-01", described);
        Assert.Contains("excluding out of stock", described);
    }

    [Fact]
    public void Missing_catalogue_names_read_as_identifiers_rather_than_as_breaks()
    {
        var lookup = new CatalogLookup(
            new Dictionary<int, ProductDto> { [1] = Product(1, "SKU-1", "Wireless Mouse", unitPrice: 25.5m, minimumStockLevel: 4) },
            new Dictionary<int, WarehouseDto> { [1] = Warehouse(1, "CAI", "Cairo Main", isActive: false) });

        Assert.Equal("Wireless Mouse", lookup.ProductName(1));
        Assert.Equal("SKU-1", lookup.Sku(1));
        Assert.Equal(4, lookup.MinimumOf(1));
        Assert.Equal(25.5m, lookup.UnitPriceOf(1));

        Assert.Equal("Product 42", lookup.ProductName(42));
        Assert.Equal("#42", lookup.Sku(42));
        Assert.Null(lookup.MinimumOf(42));

        // An inactive warehouse keeps its name for history but is not offered as a target.
        Assert.Equal("Cairo Main", lookup.WarehouseName(1));
        Assert.Empty(lookup.ActiveWarehouses);
    }

    [Theory]
    [InlineData(0, 10, "critical", "Out of stock")]
    [InlineData(10, 10, "attention", "Below minimum")]
    [InlineData(3, 10, "attention", "Below minimum")]
    [InlineData(11, 10, "ok", "Above minimum")]
    [InlineData(0, null, "muted", "No minimum set")]
    public void A_stock_level_reads_by_the_same_rule_the_alerts_use(int quantity, int? minimum, string tone, string label)
    {
        var state = Display.StockState(quantity, minimum);

        Assert.Equal((tone, label), state);
    }

    [Fact]
    public void Numbers_are_formatted_the_same_way_on_every_screen()
    {
        Assert.Equal("1,234.50", Display.Money(1234.5m));
        Assert.Equal("1,234", Display.Quantity(1234));
        Assert.Equal("+1,234", Display.Signed(1234));
        Assert.Equal("-12", Display.Signed(-12));
        Assert.Equal("12.50", Display.Amount(12.5m));
        Assert.Equal("2026-10-16 09:30", Display.Timestamp(When));
        Assert.Equal("2026-10-16", Display.DateInput(When));
        Assert.Equal(string.Empty, Display.DateInput(null));
    }

    [Fact]
    public void A_fill_bar_never_reads_as_full_when_there_is_nothing_on_hand()
    {
        Assert.Equal(2, Display.FillPercent(0, 10));
        Assert.Equal(50, Display.FillPercent(10, 10));
        Assert.Equal(100, Display.FillPercent(40, 10));
    }
}
