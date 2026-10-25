using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class WarehousesController : Controller
{
    private readonly ICatalogApi _catalogue;

    public WarehousesController(ICatalogApi catalogue)
    {
        _catalogue = catalogue;
    }

    [HttpGet("warehouses")]
    public async Task<IActionResult> Index(
        bool includeInactive = false,
        int? edit = null,
        CancellationToken cancellationToken = default)
    {
        EditWarehouseForm? editForm = null;

        if (edit is not null)
        {
            var warehouse = await _catalogue.GetWarehouseAsync(edit.Value, cancellationToken);

            if (warehouse is null)
            {
                return NotFound();
            }

            editForm = new EditWarehouseForm
            {
                Id = warehouse.Id,
                Code = warehouse.Code,
                IsActive = warehouse.IsActive,
                Name = warehouse.Name,
                Address = warehouse.Address
            };
        }

        return await ListView(new NewWarehouseForm(), editForm, includeInactive, cancellationToken);
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("warehouses/create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(
        NewWarehouseForm form,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await ListView(form, null, includeInactive, cancellationToken);
        }

        try
        {
            var created = await _catalogue.CreateWarehouseAsync(
                new CreateWarehouseRequest(form.Code, form.Name, form.Address),
                cancellationToken);

            TempData.Confirm($"Created the warehouse {created.Code} · {created.Name}.");

            return RedirectToAction(nameof(Index), new { includeInactive });
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(exception.IsConflict ? "Code" : string.Empty, exception.Explain());

            return await ListView(form, null, includeInactive, cancellationToken);
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("warehouses/{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(
        int id,
        EditWarehouseForm form,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        var warehouse = await _catalogue.GetWarehouseAsync(id, cancellationToken);

        if (warehouse is null)
        {
            return NotFound();
        }

        if (!ModelState.IsValid)
        {
            return await ListView(new NewWarehouseForm(), WithId(form, id, warehouse.Code), includeInactive, cancellationToken);
        }

        try
        {
            var updated = await _catalogue.UpdateWarehouseAsync(
                id,
                new UpdateWarehouseRequest(form.Name, form.Address),
                cancellationToken);

            TempData.Confirm($"Saved the warehouse {updated.Code} · {updated.Name}.");

            return RedirectToAction(nameof(Index), new { includeInactive });
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return await ListView(new NewWarehouseForm(), WithId(form, id, warehouse.Code), includeInactive, cancellationToken);
        }
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("warehouses/{id:int}/deactivate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Deactivate(int id, CancellationToken cancellationToken)
    {
        var warehouse = await _catalogue.GetWarehouseAsync(id, cancellationToken);
        await _catalogue.DeactivateWarehouseAsync(id, cancellationToken);

        TempData.Confirm(
            $"{warehouse?.Code ?? $"Warehouse {id}"} is inactive: it keeps its stock and its history but is not offered for new operations.");

        return RedirectToAction(nameof(Index), new { includeInactive = true });
    }

    [Authorize(Policy = ConsolePolicies.Catalogue)]
    [HttpPost("warehouses/{id:int}/activate")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Activate(int id, CancellationToken cancellationToken)
    {
        var warehouse = await _catalogue.ActivateWarehouseAsync(id, cancellationToken);

        TempData.Confirm($"{warehouse.Code} · {warehouse.Name} is active again.");

        return RedirectToAction(nameof(Index), new { includeInactive = true });
    }

    private async Task<IActionResult> ListView(
        NewWarehouseForm createForm,
        EditWarehouseForm? editForm,
        bool includeInactive,
        CancellationToken cancellationToken)
    {
        ViewData["Nav"] = "warehouses";
        ViewData["Section"] = "Catalogue";

        return View("Index", new WarehouseListViewModel
        {
            Warehouses = await _catalogue.GetWarehousesAsync(includeInactive, cancellationToken),
            IncludeInactive = includeInactive,
            CreateForm = createForm,
            EditForm = editForm
        });
    }

    private static EditWarehouseForm WithId(EditWarehouseForm form, int id, string code) => new()
    {
        Id = id,
        Code = code,
        IsActive = form.IsActive,
        Name = form.Name,
        Address = form.Address
    };
}
