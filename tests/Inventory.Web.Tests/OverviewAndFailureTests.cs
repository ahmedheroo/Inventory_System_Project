using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Controllers;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging.Abstractions;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The overview is the one screen that reads from every service at once, so it is the
/// one that says the most about what the console does with what it was given.
/// </summary>
public class OverviewTests
{
    private static (HomeController Controller, FakeReportingApi Reporting, FakeAlertsApi Alerts) Subject()
    {
        var reporting = new FakeReportingApi();
        var alerts = new FakeAlertsApi();
        var catalogue = new FakeCatalogApi();

        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse", minimumStockLevel: 10));
        catalogue.Warehouses.Add(Warehouse(1, "WH-CAI", "Cairo Main"));
        catalogue.Warehouses.Add(Warehouse(2, "WH-ALX", "Alexandria"));

        reporting.ValuationReport = new ValuationReport(
            [],
            [
                new ValuationWarehouseTotal(1, "WH-CAI", "Cairo Main", 10, 100m),
                new ValuationWarehouseTotal(2, "WH-ALX", "Alexandria", 0, 0m)
            ],
            new ValuationReportSummary(1, 2, 2, 10, 100m),
            1,
            1,
            2);

        reporting.LowStockReport = new LowStockReport(
            [
                ShortRow(1, "SKU-1", 1, "WH-CAI", 3),
                ShortRow(2, "SKU-2", 1, "WH-CAI", 0),
                ShortRow(3, "SKU-3", 1, "WH-CAI", 1),
                ShortRow(4, "SKU-4", 1, "WH-CAI", 2),
                ShortRow(5, "SKU-5", 2, "WH-ALX", 0)
            ],
            new LowStockReportSummary(4, 2, 12, 5, 2),
            1,
            100,
            5);

        reporting.MovementReport = new MovementReport(
            [
                Movement(2, 1, 1, StockTransactionType.ISSUE, -2, 8),
                Movement(1, 1, 1, StockTransactionType.RECEIVE, 10, 10)
            ],
            new MovementReportSummary(2, 10, 2, 0, 0, 0, 8),
            1,
            8,
            2);

        alerts.Alerts.AddRange(
        [
            Alert(1, 1, 1, currentQuantity: 3),
            Alert(2, 2, 1, AlertType.OUT_OF_STOCK, currentQuantity: 0),
            Alert(3, 3, 1, status: AlertStatus.RESOLVED)
        ]);

        var controller = new HomeController(
            reporting,
            alerts,
            new FakeCatalogLookup(catalogue),
            NullLogger<HomeController>.Instance).Ready();

        return (controller, reporting, alerts);
    }

    [Fact]
    public async Task The_board_gives_one_column_per_warehouse_with_what_is_short_in_it()
    {
        var (controller, _, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));

        var model = Assert.IsType<DashboardViewModel>(result.Model);

        Assert.Equal(["WH-ALX", "WH-CAI"], model.Positions.Select(position => position.Warehouse.Code));
        Assert.Equal(100m, model.Value.TotalValue);
        Assert.Equal(5, model.Shortages.AffectedProducts);

        var cairo = model.Positions.Single(position => position.Warehouse.Code == "WH-CAI");

        Assert.Equal(100m, cairo.Total!.Value);
        Assert.Equal(3, cairo.Shortest.Count);
        Assert.Equal(0, cairo.Shortest[0].Quantity);   // out of stock reads first

        var alexandria = model.Positions.Single(position => position.Warehouse.Code == "WH-ALX");

        Assert.Single(alexandria.Shortest);
    }

    [Fact]
    public async Task The_board_reads_the_shortages_and_the_alerts_it_can_show_and_no_more()
    {
        var (controller, reporting, alerts) = Subject();

        await controller.Index(CancellationToken.None);

        // The low-stock read is deep enough to fill every warehouse column, and the alert
        // read stops at the six the overview lists.
        Assert.Equal(100, reporting.LastLowStockCall!.Value.PageSize);
        Assert.Equal(6, alerts.LastQuery!.PageSize);
        Assert.Equal(AlertStatus.ACTIVE, alerts.LastQuery.Status);
        Assert.Null(alerts.LastQuery.IsRead);
    }

    [Fact]
    public async Task Open_alerts_are_counted_without_the_ones_the_service_already_resolved()
    {
        var (controller, _, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));

        var model = Assert.IsType<DashboardViewModel>(result.Model);

        Assert.Equal(2, model.OpenAlertCount);
        Assert.Equal(2, model.OpenAlerts.Count);
        Assert.All(model.OpenAlerts, alert => Assert.Equal(AlertStatus.ACTIVE, alert.Status));
    }

    [Fact]
    public async Task The_last_movements_are_shown_newest_first_as_the_service_returns_them()
    {
        var (controller, _, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index(CancellationToken.None));

        var model = Assert.IsType<DashboardViewModel>(result.Model);

        Assert.Equal([StockTransactionType.ISSUE, StockTransactionType.RECEIVE], model.RecentMovements.Select(row => row.Type));
    }
}

/// <summary>
/// A service that did not answer must never leave an empty table on screen: the filter
/// turns it into a page that names the service, and says which status the console raised.
/// </summary>
public class ApiExceptionFilterTests
{
    private static ExceptionContext Context(Exception exception)
    {
        var actionContext = new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor());

        return new ExceptionContext(actionContext, new List<IFilterMetadata>()) { Exception = exception };
    }

    private static ApiExceptionFilter Filter() => new(NullLogger<ApiExceptionFilter>.Instance);

    [Fact]
    public void An_unreachable_service_answers_503_and_the_page_says_which_one()
    {
        var context = Context(new ApiException(
            HttpStatusCode.ServiceUnavailable,
            "Reporting service",
            "The Reporting service could not be reached.",
            "Connection refused"));

        Filter().OnException(context);

        var view = Assert.IsType<ViewResult>(context.Result);

        // Named as the file is — Views/Shared/_ServiceError.cshtml, the same block the live regions
        // get alone. A name without the underscore resolves to nothing, so this fault page would
        // render as a 500 and the reason the screen could not be read would never reach the person.
        Assert.Equal("_ServiceError", view.ViewName);
        Assert.Equal(503, view.StatusCode);
        Assert.True(context.ExceptionHandled);

        var model = Assert.IsType<ServiceErrorViewModel>(view.ViewData.Model);

        Assert.Equal("Reporting service", model.Service);
        Assert.Contains("could not be reached", model.Explanation);

        // The response status is on the result, which the framework applies when the
        // view executes. Setting it here as well would be a second source of truth.
        Assert.Equal(503, view.StatusCode);
    }

    [Fact]
    public void A_service_that_answered_badly_answers_502_with_its_own_words()
    {
        var context = Context(new ApiException(
            HttpStatusCode.BadRequest,
            "Inventory service",
            "The Inventory service answered 400.",
            "Cannot issue 9 unit(s): only 4 available."));

        Filter().OnException(context);

        var view = Assert.IsType<ViewResult>(context.Result);

        Assert.Equal(502, view.StatusCode);
        Assert.Contains("only 4 available", Assert.IsType<ServiceErrorViewModel>(view.ViewData.Model).Explanation);
    }

    [Fact]
    public void Anything_that_is_not_a_service_failure_is_left_to_the_next_handler()
    {
        var context = Context(new InvalidOperationException("a real bug"));

        Filter().OnException(context);

        Assert.Null(context.Result);
        Assert.False(context.ExceptionHandled);
    }
}
