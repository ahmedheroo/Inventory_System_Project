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
/// The product pages: what the console asks the catalogue for, and what it does when
/// the Product service refuses a sku or a name.
/// </summary>
public class ProductsControllerTests
{
    private static (ProductsController Controller, FakeCatalogApi Catalogue) Subject()
    {
        var catalogue = new FakeCatalogApi();
        catalogue.Products.AddRange([Product(1, "SKU-1", "Wireless Mouse"), Product(2, "SKU-2", "Mechanical Keyboard")]);
        catalogue.Categories.Add(Category(1, "Electronics"));

        return (new ProductsController(catalogue).Ready(), catalogue);
    }

    [Fact]
    public async Task The_list_passes_the_filters_on_and_keeps_them_in_the_paging_links()
    {
        var (controller, catalogue) = Subject();

        var result = Assert.IsType<ViewResult>(await controller.Index(search: "mouse", categoryId: 1, includeInactive: true, page: 2, pageSize: 50));

        var model = Assert.IsType<ProductListViewModel>(result.Model);

        Assert.Equal("mouse", catalogue.LastQuery!.Search);
        Assert.Equal(1, catalogue.LastQuery.CategoryId);
        Assert.True(catalogue.LastQuery.IncludeInactive);
        Assert.Equal(2, catalogue.LastQuery.Page);
        Assert.Equal(50, catalogue.LastQuery.PageSize);

        Assert.Equal("/products?search=mouse&categoryId=1&includeInactive=true&pageSize=50&page=3", model.Pager.Url(3));
    }

    [Fact]
    public async Task Creating_a_product_opens_its_own_page_so_stock_can_be_received_next()
    {
        var (controller, _) = Subject();

        var result = Assert.IsType<RedirectToActionResult>(
            await controller.New(new NewProductForm
            {
                Sku = "SKU-9",
                Name = "Pallet Truck",
                UnitPrice = 120m,
                MinimumStockLevel = 2
            }, CancellationToken.None));

        Assert.Equal(nameof(ProductsController.Edit), result.ActionName);
        Assert.Equal(3, result.RouteValues!["id"]);
        Assert.Equal("Created SKU-9 · Pallet Truck.", controller.TempData["flash.message"]);
    }

    [Fact]
    public async Task A_duplicate_sku_is_reported_on_the_sku_field_it_came_from()
    {
        var (controller, catalogue) = Subject();

        catalogue.FailNextWith = new ApiException(
            HttpStatusCode.Conflict,
            "Product service",
            "The Product service answered 409.",
            "A product with sku SKU-1 already exists.");

        var result = Assert.IsType<ViewResult>(
            await controller.New(
                new NewProductForm { Sku = "SKU-1", Name = "Another mouse", UnitPrice = 1m, MinimumStockLevel = 0 },
                CancellationToken.None));

        Assert.IsType<NewProductForm>(result.Model);
        Assert.Contains("already exists", controller.ModelState["Sku"]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task An_invalid_form_comes_back_with_what_was_typed_and_the_category_list()
    {
        var (controller, _) = Subject();
        controller.ModelState.AddModelError("Name", "Enter a product name.");

        var result = Assert.IsType<ViewResult>(
            await controller.New(
                new NewProductForm { Sku = "SKU-9", Name = string.Empty, UnitPrice = 5m, MinimumStockLevel = 1 },
                CancellationToken.None));

        var model = Assert.IsType<NewProductForm>(result.Model);

        Assert.Equal("SKU-9", model.Sku);
        Assert.Single(model.Categories);
    }

    [Fact]
    public async Task A_product_that_is_not_there_answers_404_rather_than_an_empty_form()
    {
        var (controller, _) = Subject();

        Assert.IsType<NotFoundResult>(await controller.Edit(404, CancellationToken.None));
    }

    [Fact]
    public async Task A_refused_edit_keeps_the_clerk_on_the_form_with_the_service_words()
    {
        var (controller, catalogue) = Subject();

        catalogue.FailNextWith = new ApiException(
            HttpStatusCode.BadRequest,
            "Product service",
            "The Product service answered 400.",
            "A unit price cannot be negative.");

        var result = Assert.IsType<ViewResult>(
            await controller.Edit(
                1,
                new EditProductForm { Id = 1, Name = "Wireless Mouse", UnitPrice = -1m, MinimumStockLevel = 5 },
                CancellationToken.None));

        Assert.IsType<EditProductForm>(result.Model);
        Assert.Contains("cannot be negative", controller.ModelState[string.Empty]!.Errors.Single().ErrorMessage);
    }

    [Fact]
    public async Task Deactivating_keeps_the_product_and_shows_it_in_the_list_again()
    {
        var (controller, catalogue) = Subject();

        var result = Assert.IsType<RedirectToActionResult>(await controller.Deactivate(1, CancellationToken.None));

        Assert.Equal(nameof(ProductsController.Index), result.ActionName);
        Assert.Equal(true, result.RouteValues!["includeInactive"]);
        Assert.False(catalogue.Products.Single(product => product.Id == 1).IsActive);
        Assert.Equal("SKU-1 is inactive: it keeps its stock history and is hidden from the product list.", controller.TempData["flash.message"]);
    }
}
