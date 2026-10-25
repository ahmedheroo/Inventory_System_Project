using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class ProductsController : Controller
{
    private readonly ICatalogApi _catalogue;

    public ProductsController(ICatalogApi catalogue)
    {
        _catalogue = catalogue;
    }

    [HttpGet("products")]
    public async Task<IActionResult> Index(
        string? search = null,
        int? categoryId = null,
        bool includeInactive = false,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new ProductFilter(search, categoryId, includeInactive, page, pageSize);

        var products = await _catalogue.GetProductsAsync(
            new ProductQuery
            {
                Search = filter.Search,
                CategoryId = filter.CategoryId,
                IncludeInactive = filter.IncludeInactive,
                Page = filter.Page,
                PageSize = filter.PageSize
            },
            cancellationToken);

        var categories = await _catalogue.GetCategoriesAsync(cancellationToken);

        ViewData["Nav"] = "products";
        ViewData["Section"] = "Catalogue";

        return View(new ProductListViewModel
        {
            Page = products,
            Categories = categories,
            Filter = filter,
            Pager = new Pager(
                "/products",
                new Dictionary<string, string?>
                {
                    ["search"] = filter.Search,
                    ["categoryId"] = filter.CategoryId?.ToString(),
                    ["includeInactive"] = filter.IncludeInactive ? "true" : null,
                    ["pageSize"] = filter.PageSize.ToString()
                },
                products.Page,
                products.PageSize,
                products.TotalCount)
        });
    }

    // The catalogue is maintained, not used: every product page is a manager's screen, and the
    // list itself stays open to anyone who may read stock.
    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpGet("products/new")]
    public async Task<IActionResult> New(CancellationToken cancellationToken)
    {
        ViewData["Nav"] = "products";
        ViewData["Section"] = "Catalogue";

        return View(new NewProductForm { Categories = await _catalogue.GetCategoriesAsync(cancellationToken) });
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("products/new")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> New(NewProductForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(new NewProductForm
            {
                Sku = form.Sku,
                Name = form.Name,
                Description = form.Description,
                CategoryId = form.CategoryId,
                UnitPrice = form.UnitPrice,
                MinimumStockLevel = form.MinimumStockLevel,
                Categories = await _catalogue.GetCategoriesAsync(cancellationToken)
            });
        }

        try
        {
            var created = await _catalogue.CreateProductAsync(
                new CreateProductRequest(
                    form.Sku,
                    form.Name,
                    form.Description,
                    form.CategoryId,
                    form.UnitPrice,
                    form.MinimumStockLevel),
                cancellationToken);

            TempData.Confirm($"Created {created.Sku} · {created.Name}.");

            return RedirectToAction(nameof(Edit), new { id = created.Id });
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(exception.IsConflict ? "Sku" : string.Empty, exception.Explain());

            return View(new NewProductForm
            {
                Sku = form.Sku,
                Name = form.Name,
                Description = form.Description,
                CategoryId = form.CategoryId,
                UnitPrice = form.UnitPrice,
                MinimumStockLevel = form.MinimumStockLevel,
                Categories = await _catalogue.GetCategoriesAsync(cancellationToken)
            });
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpGet("products/{id:int}/edit")]
    public async Task<IActionResult> Edit(int id, CancellationToken cancellationToken)
    {
        var product = await _catalogue.GetProductAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        ViewData["Nav"] = "products";
        ViewData["Section"] = "Catalogue";

        return View(await EditFormOf(product, cancellationToken));
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("products/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, EditProductForm form, CancellationToken cancellationToken)
    {
        var product = await _catalogue.GetProductAsync(id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return View(await EditFormOf(product, cancellationToken, form));
        }

        try
        {
            var updated = await _catalogue.UpdateProductAsync(
                id,
                new UpdateProductRequest(
                    form.Name,
                    form.Description,
                    form.CategoryId,
                    form.UnitPrice,
                    form.MinimumStockLevel),
                cancellationToken);

            TempData.Confirm($"Saved {updated.Sku} · {updated.Name}.");

            return RedirectToAction(nameof(Index));
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return View(await EditFormOf(product, cancellationToken, form));
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("products/{id:int}/sku")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeSku(int id, string sku, CancellationToken cancellationToken)
    {
        try
        {
            var updated = await _catalogue.ChangeSkuAsync(id, sku, cancellationToken);

            TempData.Confirm($"The sku is now {updated.Sku}.");
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            TempData.Warn(exception.Explain());
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("products/{id:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var product = await _catalogue.GetProductAsync(id, cancellationToken);
        await _catalogue.DeactivateProductAsync(id, cancellationToken);

        TempData.Confirm($"{product?.Sku ?? $"Product {id}"} is inactive: it keeps its stock history and is hidden from the product list.");

        return RedirectToAction(nameof(Index), new { includeInactive = true });
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("products/{id:int}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var product = await _catalogue.ActivateProductAsync(id, cancellationToken);

        TempData.Confirm($"{product.Sku} · {product.Name} is active again.");

        return RedirectToAction(nameof(Edit), new { id });
    }

    private async Task<EditProductForm> EditFormOf(
        ProductDto product,
        CancellationToken cancellationToken,
        EditProductForm? typed = null) =>
        new()
        {
            Id = product.Id,
            Sku = product.Sku,
            IsActive = product.IsActive,
            Name = typed?.Name ?? product.Name,
            Description = typed is null ? product.Description : typed.Description,
            CategoryId = typed is null ? product.CategoryId : typed.CategoryId,
            UnitPrice = typed?.UnitPrice ?? product.UnitPrice,
            MinimumStockLevel = typed?.MinimumStockLevel ?? product.MinimumStockLevel,
            Categories = await _catalogue.GetCategoriesAsync(cancellationToken)
        };
}
