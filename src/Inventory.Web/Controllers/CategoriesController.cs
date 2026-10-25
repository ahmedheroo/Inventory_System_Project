using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class CategoriesController : Controller
{
    private readonly ICatalogApi _catalogue;

    public CategoriesController(ICatalogApi catalogue)
    {
        _catalogue = catalogue;
    }

    [HttpGet("categories")]
    public async Task<IActionResult> Index(int? edit = null, CancellationToken cancellationToken = default)
    {
        CategoryFormViewModel? editForm = null;

        if (edit is not null)
        {
            var categories = await _catalogue.GetCategoriesAsync(cancellationToken);
            var category = categories.FirstOrDefault(candidate => candidate.Id == edit);

            if (category is null)
            {
                return NotFound();
            }

            editForm = new CategoryFormViewModel
            {
                Id = category.Id,
                Name = category.Name,
                Description = category.Description
            };
        }

        return await ListView(new CategoryFormViewModel(), editForm, cancellationToken);
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("categories/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(CategoryFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ListView(form, null, cancellationToken);
        }

        try
        {
            var created = await _catalogue.CreateCategoryAsync(
                new CreateCategoryRequest(form.Name, form.Description),
                cancellationToken);

            TempData.Confirm($"Created the category {created.Name}.");

            return RedirectToAction(nameof(Index));
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return await ListView(form, null, cancellationToken);
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("categories/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, CategoryFormViewModel form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ListView(new CategoryFormViewModel(), WithId(form, id), cancellationToken);
        }

        try
        {
            var updated = await _catalogue.UpdateCategoryAsync(
                id,
                new UpdateCategoryRequest(form.Name, form.Description),
                cancellationToken);

            TempData.Confirm($"Saved the category {updated.Name}.");

            return RedirectToAction(nameof(Index));
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return await ListView(new CategoryFormViewModel(), WithId(form, id), cancellationToken);
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("categories/{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            await _catalogue.DeleteCategoryAsync(id, cancellationToken);

            TempData.Confirm("The category was deleted. Products in it keep their stock and are shown as uncategorised.");
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            TempData.Warn(exception.Explain());
        }

        return RedirectToAction(nameof(Index));
    }

    private static CategoryFormViewModel WithId(CategoryFormViewModel form, int id) => new()
    {
        Id = id,
        Name = form.Name,
        Description = form.Description
    };

    private async Task<IActionResult> ListView(
        CategoryFormViewModel createForm,
        CategoryFormViewModel? editForm,
        CancellationToken cancellationToken)
    {
        ViewData["Nav"] = "categories";
        ViewData["Section"] = "Catalogue";

        return View("Index", new CategoryListViewModel
        {
            Categories = await _catalogue.GetCategoriesAsync(cancellationToken),
            CreateForm = createForm,
            EditForm = editForm
        });
    }
}
