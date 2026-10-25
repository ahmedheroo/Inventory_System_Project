using Inventory.Web.Api.Contracts;
using Inventory.Web.Controllers;
using Inventory.Web.Models;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// Alerts are read and acknowledged, never created. The two things worth guarding are
/// the default view (active, not history) and where an acknowledgement returns to.
/// </summary>
public class AlertsControllerTests
{
    private static (AlertsController Controller, FakeAlertsApi Alerts) Subject()
    {
        var alerts = new FakeAlertsApi();
        var catalogue = new FakeCatalogApi();

        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse", minimumStockLevel: 10));
        catalogue.Warehouses.Add(Warehouse(1, "WH-CAI", "Cairo Main"));

        alerts.Alerts.AddRange(
        [
            Alert(1, 1, 1, currentQuantity: 4),
            Alert(2, 1, 1, AlertType.OUT_OF_STOCK, currentQuantity: 0),
            Alert(3, 1, 1, isRead: true, status: AlertStatus.RESOLVED)
        ]);

        return (new AlertsController(alerts, new FakeCatalogLookup(catalogue)).Ready(), alerts);
    }

    [Fact]
    public async Task The_screen_opens_on_the_alerts_that_are_still_active()
    {
        var (controller, alerts) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index());

        var model = Assert.IsType<AlertListViewModel>(result.Model);

        Assert.Equal(AlertStatus.ACTIVE, alerts.LastQuery!.Status);
        Assert.Null(alerts.LastQuery.IsRead);
        Assert.Equal(2, model.Page.TotalCount);
        Assert.Equal(AlertStatus.ACTIVE, model.Filter.Status);
    }

    [Fact]
    public async Task The_filter_keys_read_the_way_the_page_writes_them()
    {
        var (controller, alerts) = Subject();

        await controller.Index(status: "all", read: "unread", type: AlertType.OUT_OF_STOCK, warehouseId: 1, productId: 1);

        Assert.Null(alerts.LastQuery!.Status);
        Assert.False(alerts.LastQuery.IsRead);
        Assert.Equal(AlertType.OUT_OF_STOCK, alerts.LastQuery.Type);
        Assert.Equal(1, alerts.LastQuery.WarehouseId);
    }

    [Fact]
    public async Task Acknowledging_one_alert_returns_to_the_view_it_was_acknowledged_from()
    {
        var (controller, alerts) = Subject();

        var result = Assert.IsType<RedirectResult>(await controller.MarkRead(1, "/alerts?status=all&page=2", CancellationToken.None));

        Assert.Equal("/alerts?status=all&page=2", result.Url);
        Assert.Equal([1], alerts.Acknowledged);
        // The minimum in the message is the one the alert recorded, not the one the
        // product carries today: an acknowledged alert is a statement about its moment.
        Assert.Equal(
            "Acknowledged: Wireless Mouse is 4 in Cairo Main, against a minimum of 5.",
            controller.TempData["flash.message"]);
    }

    [Theory]
    [InlineData("https://example.com/steal")]
    [InlineData("//example.com/steal")]
    [InlineData("/products")]
    [InlineData(null)]
    public async Task An_acknowledgement_only_ever_returns_to_the_alerts_screen(string? back)
    {
        var (controller, _) = Subject();

        var result = Assert.IsType<RedirectResult>(await controller.MarkRead(1, back, CancellationToken.None));

        Assert.Equal("/alerts", result.Url);
    }

    [Fact]
    public async Task Acknowledge_all_covers_a_batch_and_says_how_much_of_the_list_it_covered()
    {
        var (controller, alerts) = Subject();

        alerts.Alerts.Clear();

        for (var id = 1; id <= 60; id++)
        {
            alerts.Alerts.Add(Alert(id, 1, 1, currentQuantity: 1));
        }

        var result = Assert.IsType<RedirectResult>(await controller.MarkAllRead("/alerts?read=unread", CancellationToken.None));

        Assert.Equal("/alerts?read=unread", result.Url);
        Assert.Equal(50, alerts.Acknowledged.Count);
        Assert.Equal("Acknowledged 50 of 60 unread alerts. Acknowledge again for the next batch.", controller.TempData["flash.message"]);
    }

    [Fact]
    public async Task A_view_with_nothing_unread_acknowledges_nothing_and_says_so()
    {
        var (controller, alerts) = Subject();

        alerts.Alerts.Clear();
        alerts.Alerts.Add(Alert(9, 1, 1, isRead: true));

        await controller.MarkAllRead("/alerts", CancellationToken.None);

        Assert.Empty(alerts.Acknowledged);
        Assert.Equal("There was nothing unread to acknowledge on this view.", controller.TempData["flash.message"]);
    }
}
