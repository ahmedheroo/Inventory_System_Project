using Inventory.Web.Api.Contracts;
using Inventory.Web.Controllers;
using Inventory.Web.Models;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The reports and their downloads. The download has to be the report on screen — same
/// filters, every matching row — which is why the queries are forwarded verbatim.
/// </summary>
public class ReportsControllerTests
{
    private static (ReportsController Controller, FakeReportingApi Reporting) Subject()
    {
        var reporting = new FakeReportingApi();
        var catalogue = new FakeCatalogApi();

        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse", categoryId: 1));
        catalogue.Warehouses.Add(Warehouse(1, "WH-CAI", "Cairo Main"));
        catalogue.Categories.Add(Category(1, "Electronics", productCount: 1));

        reporting.ValuationReport = new ValuationReport(
            [new ValuationReportRow(1, "SKU-1", "Wireless Mouse", 1, 1, "WH-CAI", "Cairo Main", 4, 10m, 40m)],
            [new ValuationWarehouseTotal(1, "WH-CAI", "Cairo Main", 4, 40m)],
            new ValuationReportSummary(1, 1, 1, 4, 40m),
            1,
            20,
            1);

        reporting.LowStockReport = new LowStockReport(
            [ShortRow(1, "SKU-1", 1, "WH-CAI", 0)],
            new LowStockReportSummary(0, 1, 5, 1, 1),
            1,
            20,
            1);

        var controller = new ReportsController(reporting, new FakeCatalogLookup(catalogue), catalogue).Ready();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return (controller, reporting);
    }

    [Fact]
    public async Task Applying_a_filter_keeps_the_page_size_and_resets_the_page()
    {
        var (controller, reporting) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Inventory(productId: 1, warehouseId: 1, page: 3, pageSize: 50));

        var model = Assert.IsType<InventoryReportViewModel>(result.Model);

        Assert.Equal((1, 1, 3, 50), reporting.LastInventoryCall);
        // Paging keeps the size it is paging: only the page number moves.
        Assert.Equal("/reports/inventory?productId=1&warehouseId=1&pageSize=50&page=2", model.Pager.Url(2));
        Assert.Equal("/reports/inventory/export?productId=1&warehouseId=1&format=csv", model.Filters.DownloadUrl("inventory", "csv"));
    }

    [Fact]
    public async Task The_movement_report_asks_for_the_whole_of_the_day_it_was_given()
    {
        var (controller, reporting) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Movement(
            warehouseId: 1,
            type: StockTransactionType.ISSUE,
            from: new DateTime(2026, 10, 1),
            to: new DateTime(2026, 10, 16)));

        Assert.Equal(StockTransactionType.ISSUE, reporting.LastMovementCall!.Value.Type);
        Assert.Equal(new DateTime(2026, 10, 1), reporting.LastMovementCall.Value.From);
        Assert.Equal(new DateTime(2026, 10, 16, 23, 59, 59, 999).AddTicks(9999), reporting.LastMovementCall.Value.To);

        var model = Assert.IsType<MovementReportViewModel>(result.Model);

        Assert.Equal("Movement ledger", controller.ViewData["Title"]);
        Assert.Contains("to 2026-10-16", model.Filters.Describe(model.Catalog));
    }

    [Fact]
    public async Task The_short_stock_report_can_leave_out_what_is_not_there_at_all()
    {
        var (controller, reporting) = Subject();

        await controller.LowStock(includeOutOfStock: false);

        Assert.False(reporting.LastLowStockCall!.Value.IncludeOutOfStock);
    }

    [Fact]
    public async Task The_valuation_carries_what_each_warehouse_is_worth()
    {
        var (controller, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Valuation(categoryId: 1));

        var model = Assert.IsType<ValuationReportViewModel>(result.Model);

        Assert.Equal(40m, model.Report.Summary.TotalValue);
        Assert.Single(model.Report.ByWarehouse);
        Assert.Single(model.Categories);
    }

    [Fact]
    public async Task A_download_is_the_report_on_screen_with_every_row_of_it()
    {
        var (controller, reporting) = Subject();

        controller.HttpContext.Request.QueryString = new QueryString("?warehouseId=1&type=ISSUE&format=csv&page=3");

        var result = Assert.IsType<FileContentResult>(await controller.Export("movement", CancellationToken.None));

        Assert.Equal("/api/reports/movement/export?warehouseId=1&type=ISSUE&format=csv&page=3", reporting.LastDownload);
        Assert.Equal("text/csv", result.ContentType);
        Assert.Equal("movement-report.csv", result.FileDownloadName);
        Assert.Equal([1, 2, 3], result.FileContents);
    }

    [Fact]
    public async Task An_unknown_report_or_a_format_nobody_asked_for_is_refused_here_not_by_the_service()
    {
        var (controller, reporting) = Subject();

        controller.HttpContext.Request.QueryString = new QueryString("?format=pdf");
        Assert.IsType<BadRequestResult>(await controller.Export("inventory", CancellationToken.None));

        controller.HttpContext.Request.QueryString = new QueryString("?format=csv");
        Assert.IsType<BadRequestResult>(await controller.Export("everything", CancellationToken.None));

        Assert.Null(reporting.LastDownload);
    }

    [Fact]
    public void The_reports_landing_page_opens_the_first_report()
    {
        var (controller, _) = Subject();

        var result = Assert.IsType<RedirectToActionResult>(controller.Index());

        Assert.Equal(nameof(ReportsController.Inventory), result.ActionName);
    }
}
