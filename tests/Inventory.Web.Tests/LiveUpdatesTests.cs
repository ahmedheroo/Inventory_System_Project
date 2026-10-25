using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Components;
using Inventory.Web.Controllers;
using Inventory.Web.Hubs;
using Inventory.Web.Models;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewComponents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The live view (Sprint 7): what the browser re-reads when the Inventory hub says a level
/// moved, and the rail's alert count that keeps itself current.
///
/// The tests come in two kinds. The first drives the fragment endpoints the live client calls
/// — they must answer with exactly the region the page renders, under the filters the page is
/// showing. The second reads the pages and the layout as text and checks that the wiring agrees
/// with those endpoints: a region whose source does not exist is a screen that silently stops
/// moving, and nothing else would fail.
/// </summary>
public class LiveUpdatesTests
{
    // ------------------------------------------------------------- fragments

    private static (HomeController Controller, FakeAlertsApi Alerts) Dashboard()
    {
        var reporting = new FakeReportingApi();
        var alerts = new FakeAlertsApi();
        var catalogue = new FakeCatalogApi();

        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse", minimumStockLevel: 10));
        catalogue.Warehouses.Add(Warehouse(1, "WH-CAI", "Cairo Main"));

        reporting.ValuationReport = new ValuationReport(
            [],
            [new ValuationWarehouseTotal(1, "WH-CAI", "Cairo Main", 10, 100m)],
            new ValuationReportSummary(1, 1, 1, 10, 100m),
            1,
            20,
            1);

        reporting.LowStockReport = new LowStockReport(
            [ShortRow(1, "SKU-1", 1, "WH-CAI", 4, minimum: 10)],
            new LowStockReportSummary(1, 0, 6, 1, 1),
            1,
            20,
            1);

        reporting.MovementReport = new MovementReport(
            [Movement(1, 1, 1, StockTransactionType.RECEIVE, 4, 4)],
            new MovementReportSummary(1, 4, 0, 0, 0, 0, 4),
            1,
            20,
            1);

        alerts.Alerts.Add(Alert(1, 1, 1, currentQuantity: 4));
        alerts.Alerts.Add(Alert(2, 1, 1, AlertType.OUT_OF_STOCK, currentQuantity: 0));

        return (new HomeController(reporting, alerts, new FakeCatalogLookup(catalogue), NullLogger<HomeController>.Instance).Ready(), alerts);
    }

    [Fact]
    public async Task The_overview_fragment_is_the_board_itself_and_not_a_second_page()
    {
        var (controller, _) = Dashboard();

        var result = Assert.IsType<PartialViewResult>(await controller.LiveOverview(CancellationToken.None));

        Assert.Equal("_Overview", result.ViewName);

        var model = Assert.IsType<DashboardViewModel>(result.Model);

        // The same reads the page makes: the fragment is not a lighter screen, it is the screen.
        Assert.Equal(2, model.OpenAlertCount);
        Assert.Equal(2, model.OpenAlerts.Count);
        Assert.Single(model.Positions);
        Assert.Single(model.ShortStock);
        Assert.Single(model.RecentMovements);
    }

    [Fact]
    public async Task The_levels_fragment_reads_the_view_the_page_is_showing()
    {
        var inventory = new FakeInventoryApi();
        var catalogue = new FakeCatalogApi();

        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse"));
        catalogue.Warehouses.Add(Warehouse(2, "WH-ALX", "Alexandria"));

        inventory.Stock.Add(new InventoryDto(4, 1, 2, 9, When));

        var controller = new InventoryController(inventory, new FakeCatalogLookup(catalogue)).Ready();

        var result = Assert.IsType<PartialViewResult>(
            await controller.LiveLevels(productId: 1, warehouseId: 2, pageSize: 50, focus: 4, cancellationToken: CancellationToken.None));

        Assert.Equal("_Levels", result.ViewName);

        var model = Assert.IsType<StockListViewModel>(result.Model);

        Assert.Equal(1, inventory.LastQuery!.ProductId);        // the filters travel
        Assert.Equal(2, inventory.LastQuery.WarehouseId);
        Assert.Equal(50, inventory.LastQuery.PageSize);
        Assert.Equal(4, model.Focus);                           // and so does the row to point at
        Assert.Single(model.Page.Items);
        Assert.Equal(9, model.TotalQuantity);
    }

    [Fact]
    public void The_rails_count_is_a_view_component_so_both_the_page_and_the_fragment_render_it()
    {
        var alerts = new FakeAlertsApi();
        var catalogue = new FakeCatalogApi();
        var controller = new AlertsController(alerts, new FakeCatalogLookup(catalogue)).Ready();

        var result = Assert.IsType<ViewComponentResult>(controller.LiveAttention());

        Assert.Equal("AttentionBadge", result.ViewComponentName);
    }

    // ---------------------------------------------------------------- badge

    private static AttentionBadgeViewComponent Badge(FakeAlertsApi alerts) =>
        new(alerts, NullLogger<AttentionBadgeViewComponent>.Instance);

    private static async Task<AttentionNotice> Notice(FakeAlertsApi alerts)
    {
        var result = Assert.IsType<ViewViewComponentResult>(await Badge(alerts).InvokeAsync());

        return Assert.IsType<AttentionNotice>(result.ViewData!.Model);
    }

    [Fact]
    public async Task The_count_is_every_open_alert_not_just_the_ones_the_badge_read()
    {
        var alerts = new FakeAlertsApi();
        alerts.Alerts.AddRange([Alert(1, 1, 1), Alert(2, 1, 1), Alert(3, 1, 1, status: AlertStatus.RESOLVED)]);

        for (var id = 4; id <= 45; id++)
        {
            alerts.Alerts.Add(Alert(id, 1, 1));
        }

        var notice = await Notice(alerts);

        // 45 alerts were recorded and one of them is resolved: the badge reads 44 even though
        // it was handed 20 rows, because the count is the service's own total.
        Assert.Equal(44, notice.ActiveCount);
        Assert.Equal(AlertStatus.ACTIVE, alerts.LastQuery!.Status); // history is not "attention"
        Assert.Equal(20, alerts.LastQuery.PageSize);
        Assert.False(notice.AnyOutOfStock);
    }

    [Fact]
    public async Task The_tone_follows_the_worst_open_alert()
    {
        var alerts = new FakeAlertsApi();
        alerts.Alerts.Add(Alert(1, 1, 1, currentQuantity: 4));
        alerts.Alerts.Add(Alert(2, 1, 1, AlertType.OUT_OF_STOCK, currentQuantity: 0));

        var notice = await Notice(alerts);

        Assert.True(notice.AnyOutOfStock);
        Assert.Null(notice.Unavailable);
    }

    [Fact]
    public async Task An_alert_service_that_cannot_be_read_says_so_rather_than_showing_zero()
    {
        var alerts = new FakeAlertsApi
        {
            FailWith = new ApiException(
                HttpStatusCode.ServiceUnavailable,
                "Alert service",
                "The Alert service could not be reached.",
                "No connection could be made.")
        };

        var notice = await Notice(alerts);

        Assert.Equal("Alert service", notice.Unavailable);
        Assert.Equal(0, notice.ActiveCount);
    }

    // -------------------------------------------------------------- wiring

    [Fact]
    public void The_browser_follows_the_console_and_the_relay_follows_the_inventory_service()
    {
        var defaults = ServiceApiOptions.FromConfiguration(new ConfigurationBuilder().Build());

        // The relay's address: a server-to-server one, reached with the console's own token.
        Assert.Equal("http://localhost:5102/hubs/stock", defaults.InventoryHub.ToString());

        // The browser's: this console's own hub, behind the session cookie it already has.
        Assert.Equal("/hubs/live", LiveHub.Path);

        var configured = ServiceApiOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:InventoryApi:BaseUrl"] = "http://inventory.internal:8080/",
                ["Services:InventoryApi:HubUrl"] = "https://console.example.com/stock-hub"
            })
            .Build());

        Assert.Equal("http://inventory.internal:8080/", configured.InventoryApi.ToString());
        Assert.Equal("https://console.example.com/stock-hub", configured.InventoryHub.ToString());
    }

    [Fact]
    public void Every_live_region_asks_for_a_fragment_that_exists()
    {
        var root = RepositoryRoot();

        var layout = Read(root, "src/Inventory.Web/Views/Shared/_Layout.cshtml");
        var overview = Read(root, "src/Inventory.Web/Views/Home/Index.cshtml");
        var levels = Read(root, "src/Inventory.Web/Views/Inventory/Index.cshtml");

        // The rail's count, on every screen, with the wait the alert for a movement needs.
        Assert.Contains("data-live=\"attention\"", layout);
        Assert.Contains("data-live-source=\"/live/attention\"", layout);
        Assert.Contains("Component.InvokeAsync(\"AttentionBadge\")", layout);
        Assert.Contains("data-live-state", layout);
        Assert.Contains("~/lib/signalr/signalr.min.js", layout);
        Assert.Contains("~/js/live.js", layout);

        // The page follows the console's own hub, not the Inventory service's: that one wants a
        // token, and this hub wants the session the page already has. Both halves are named on the
        // layout, so a page that follows nothing is a change here rather than a mystery at runtime.
        Assert.Contains("data-live-hub=\"@(signedIn ? LiveHub.Path : null)\"", layout);
        Assert.Contains("@if (signedIn)", layout);

        // The two regions that follow stock, each naming the fragment its page renders too.
        Assert.Contains("data-live-source=\"/live/overview\"", overview);
        Assert.Contains("<partial name=\"_Overview\"", overview);
        Assert.Contains("data-live-source=\"/live/levels\"", levels);
        Assert.Contains("data-live-query=\"true\"", levels);       // the filters travel with the refresh
        Assert.Contains("<partial name=\"_Levels\"", levels);

        // And the pieces those routes resolve to.
        Assert.True(File.Exists(Path.Combine(root, "src/Inventory.Web/Views/Home/_Overview.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "src/Inventory.Web/Views/Inventory/_Levels.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "src/Inventory.Web/Views/Shared/Components/AttentionBadge/Default.cshtml")));
        Assert.True(File.Exists(Path.Combine(root, "src/Inventory.Web/wwwroot/lib/signalr/signalr.min.js")));

        // The client listens for the notification the hub sends under that name, and recognises its
        // own movements coming back by the station the layout hands it.
        var client = Read(root, "src/Inventory.Web/wwwroot/js/live.js");

        Assert.Contains("StockChanged", client);
        Assert.Contains("RelayState", client);             // whether the console is hearing anything
        Assert.Contains("dataset.liveStation", client);   // the station the layout hands it
        Assert.Contains("is-mine", client);               // and how it marks its own movement
        Assert.Contains("withCredentials: true", client);  // the session cookie has to travel
        Assert.Contains("data-live-station=\"@(signedIn ? Station.Id : null)\"", layout);
    }

    [Fact]
    public void The_attribution_header_is_the_one_the_inventory_service_reads()
    {
        // The two sides are separate projects on purpose (the console references no service), so the
        // one thing they must agree on is pinned here and in the service's own test.
        Assert.Equal("X-Origin-Id", StationHeaderHandler.HeaderName);
    }

    /// <summary>The repository root, found by its marker rather than by assuming where the tests run from.</summary>
    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "InventorySystem.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        return directory!.FullName;
    }

    private static string Read(string root, string relativePath) =>
        File.ReadAllText(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
}
