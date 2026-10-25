using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Controllers;
using Inventory.Web.Models;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Mvc;
using static InventorySystem.Web.Tests.TestData;

namespace InventorySystem.Web.Tests;

/// <summary>
/// Stock: where an operation lands a clerk, and what happens when the Inventory service
/// refuses one. A refusal must leave the typed quantity in place — a clerk on a shared
/// terminal should never have to type a delivery note twice.
/// </summary>
public class InventoryControllerTests
{
    private static (InventoryController Controller, FakeInventoryApi Inventory, FakeCatalogApi Catalogue) Subject()
    {
        var inventory = new FakeInventoryApi();
        var catalogue = new FakeCatalogApi();
        catalogue.Products.Add(Product(1, "SKU-1", "Wireless Mouse", minimumStockLevel: 5));
        catalogue.Warehouses.Add(Warehouse(1, "WH-CAI", "Cairo Main"));
        catalogue.Warehouses.Add(Warehouse(2, "WH-ALX", "Alexandria"));
        inventory.Stock.Add(new InventoryDto(11, 1, 1, 4, When));

        return (new InventoryController(inventory, new FakeCatalogLookup(catalogue)).Ready(), inventory, catalogue);
    }

    [Fact]
    public async Task Receiving_ends_on_the_row_it_changed_and_reports_the_new_level()
    {
        var (controller, inventory, _) = Subject();

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.Receive(
                new ReceiveStockForm { ProductId = 1, WarehouseId = 1, Quantity = 6, Reference = "Delivery 88" },
                CancellationToken.None));

        Assert.Equal(nameof(InventoryController.Index), result.ActionName);
        Assert.Equal(1, result.RouteValues!["productId"]);
        Assert.Equal(1, result.RouteValues!["warehouseId"]);
        Assert.Equal(11, result.RouteValues!["focus"]);

        Assert.Equal("Delivery 88", inventory.LastReceive!.Reference);
        Assert.Equal("Received 6 of Wireless Mouse into Cairo Main. Cairo Main now holds 10.", controller.TempData["flash.message"]);
    }

    [Fact]
    public async Task A_blank_reference_is_not_written_into_the_ledger()
    {
        var (controller, inventory, _) = Subject();

        await controller.Receive(
            new ReceiveStockForm { ProductId = 1, WarehouseId = 1, Quantity = 1, Reference = "   " },
            CancellationToken.None);

        Assert.Null(inventory.LastReceive!.Reference);
    }

    [Fact]
    public async Task A_refused_issue_stays_on_the_form_with_the_service_words_and_the_typed_quantity()
    {
        var (controller, inventory, _) = Subject();

        inventory.RefuseWith = new ApiException(
            HttpStatusCode.BadRequest,
            "Inventory service",
            "The Inventory service answered 400.",
            "Cannot issue 9 unit(s): only 4 available for product 1 in warehouse 1.");

        var form = new IssueStockForm { ProductId = 1, WarehouseId = 1, Quantity = 9, Reference = "Order 771" };

        var result = Assert.IsType<ViewResult>(await controller.Issue(form, CancellationToken.None));

        Assert.Equal("Issue", result.ViewName);

        var returned = Assert.IsType<IssueStockForm>(result.Model);

        Assert.Equal(9, returned.Quantity);
        Assert.Equal("Order 771", returned.Reference);
        Assert.Equal(1, returned.ProductId);
        Assert.Contains("only 4 available", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task A_transfer_to_the_same_warehouse_is_stopped_before_the_service_is_asked()
    {
        var (controller, inventory, _) = Subject();

        var form = new TransferStockForm { ProductId = 1, Quantity = 2, SourceWarehouseId = 1, DestinationWarehouseId = 1 };

        var result = Assert.IsType<ViewResult>(await controller.Transfer(form, CancellationToken.None));

        Assert.Equal("Transfer", result.ViewName);
        Assert.Null(inventory.LastTransfer);
        Assert.Contains("two different warehouses", controller.ModelState["DestinationWarehouseId"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task A_transfer_opens_the_source_warehouse_afterwards()
    {
        var (controller, inventory, _) = Subject();
        inventory.Stock.Add(new InventoryDto(12, 1, 2, 0, When));

        var form = new TransferStockForm { ProductId = 1, Quantity = 3, SourceWarehouseId = 1, DestinationWarehouseId = 2 };

        var result = Assert.IsType<RedirectToActionResult>(await controller.Transfer(form, CancellationToken.None));

        Assert.Equal(1, result.RouteValues!["warehouseId"]);
        Assert.Equal("Transferred 3 of Wireless Mouse from Cairo Main to Alexandria. The source now holds 1.", controller.TempData["flash.message"]);
    }

    [Fact]
    public async Task A_count_is_recorded_as_the_counted_quantity_not_as_the_default_quantity()
    {
        var (controller, inventory, _) = Subject();

        await controller.Adjust(
            new AdjustStockForm { ProductId = 1, WarehouseId = 1, NewQuantity = 40, Reference = "Count 16 Oct" },
            CancellationToken.None);

        Assert.Equal(40, inventory.LastAdjust!.NewQuantity);
        Assert.Equal("Count 16 Oct", inventory.LastAdjust.Reference);
        Assert.Equal("Recorded the count for Wireless Mouse in Cairo Main: the ledger now reads 40.", controller.TempData["flash.message"]);
    }

    [Fact]
    public async Task An_operation_page_opens_with_the_product_and_warehouse_it_was_linked_with()
    {
        var (controller, _, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Receive(productId: 1, warehouseId: 2));

        var form = Assert.IsType<ReceiveStockForm>(result.Model);

        Assert.Equal(1, form.ProductId);
        Assert.Equal(2, form.WarehouseId);
        Assert.NotEmpty(form.Catalog.ActiveProducts);
        Assert.Equal("Receive stock", controller.ViewData["Title"]);
    }

    [Fact]
    public async Task The_ledger_widens_the_end_date_so_the_whole_day_is_included()
    {
        var (controller, inventory, _) = Subject();

        await controller.History(productId: 1, from: new DateTime(2026, 10, 1), to: new DateTime(2026, 10, 16));

        Assert.Equal(new DateTime(2026, 10, 1), inventory.LastHistoryQuery!.From);
        Assert.Equal(new DateTime(2026, 10, 16, 23, 59, 59, 999).AddTicks(9999), inventory.LastHistoryQuery.To);
    }

    [Fact]
    public async Task The_levels_page_marks_the_row_an_operation_touched()
    {
        var (controller, _, _) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index(productId: 1, warehouseId: 1, focus: 11));

        var model = Assert.IsType<StockListViewModel>(result.Model);

        Assert.Equal(11, model.Focus);
        Assert.Equal(4, model.TotalQuantity);
        Assert.Single(model.Page.Items);
    }
}
