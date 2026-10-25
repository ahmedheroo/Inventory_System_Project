using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Infrastructure;
using Inventory.Web.Models;
using Inventory.Web.Security;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

 
/// Stock: the levels on hand, the movement ledger, and the four operations that write
/// to them. Every operation ends on the levels page filtered to the row it changed, so
/// a clerk sees the effect of the entry they just made instead of a confirmation nobody
/// reads.
 
public sealed class InventoryController : Controller
{
    private readonly IInventoryApi _inventory;
    private readonly ICatalogLookupService _catalogue;

    public InventoryController(IInventoryApi inventory, ICatalogLookupService catalogue)
    {
        _inventory = inventory;
        _catalogue = catalogue;
    }

    // levels

    [HttpGet("inventory")]
    public async Task<IActionResult> Index(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        int? focus = null,
        CancellationToken cancellationToken = default)
    {
        ViewData["Nav"] = "levels";
        ViewData["Section"] = "Stock";
        ViewData["Note"] = "Quantities the Inventory holds per product and warehouse. Rows in red are at or below the product's minimum.";

        return View(await LevelsAsync(productId, warehouseId, page, pageSize, focus, cancellationToken));
    }

 
    /// The stock table on its own: what the live client re-reads when the hub says a level moved.
    /// The caller sends the query string the page is currently showing, so a refresh keeps the
    /// filters, the page and the sort the person behind the screen chose.
 
    [HttpGet("/live/levels")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LiveLevels(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        int? focus = null,
        CancellationToken cancellationToken = default) =>
        PartialView("_Levels", await LevelsAsync(productId, warehouseId, page, pageSize, focus, cancellationToken));

    ///  Reads the levels screen from the Inventory service and the catalogue 
    private async Task<StockListViewModel> LevelsAsync(
        int? productId,
        int? warehouseId,
        int page,
        int pageSize,
        int? focus,
        CancellationToken cancellationToken)
    {
        var filter = new StockFilter(productId, warehouseId, page, pageSize);

        var stock = await _inventory.GetStockAsync(
            new InventoryQuery
            {
                ProductId = filter.ProductId,
                WarehouseId = filter.WarehouseId,
                Page = filter.Page,
                PageSize = filter.PageSize
            },
            cancellationToken);

        var catalogue = await _catalogue.GetAsync(cancellationToken);

        return new StockListViewModel
        {
            Page = stock,
            Catalog = catalogue,
            Filter = filter,
            Pager = new Pager(
                "/inventory",
                new Dictionary<string, string?>
                {
                    ["productId"] = filter.ProductId?.ToString(),
                    ["warehouseId"] = filter.WarehouseId?.ToString(),
                    ["pageSize"] = filter.PageSize.ToString()
                },
                stock.Page,
                stock.PageSize,
                stock.TotalCount),
            Focus = focus
        };
    }

    //   ledger

    [HttpGet("inventory/history")]
    public async Task<IActionResult> History(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filter = new HistoryFilter(productId, warehouseId, type, from, to, page, pageSize);

        var history = await _inventory.GetHistoryAsync(
            new HistoryQuery
            {
                ProductId = filter.ProductId,
                WarehouseId = filter.WarehouseId,
                Type = filter.Type,
                From = filter.From,
                To = EndOf(filter.To),
                Page = filter.Page,
                PageSize = filter.PageSize
            },
            cancellationToken);

        var catalogue = await _catalogue.GetAsync(cancellationToken);

        ViewData["Nav"] = "history";
        ViewData["Section"] = "Stock";
        ViewData["Note"] = "Every stock entry the Inventory service recorded, newest first. Dates are UTC days and include the day you type.";

        return View(new HistoryListViewModel
        {
            Page = history,
            Catalog = catalogue,
            Filter = filter,
            Pager = new Pager(
                "/inventory/history",
                new Dictionary<string, string?>
                {
                    ["productId"] = filter.ProductId?.ToString(),
                    ["warehouseId"] = filter.WarehouseId?.ToString(),
                    ["type"] = filter.Type?.ToString(),
                    ["from"] = Display.DateInput(filter.From),
                    ["to"] = Display.DateInput(filter.To),
                    ["pageSize"] = filter.PageSize.ToString()
                },
                history.Page,
                history.PageSize,
                history.TotalCount)
        });
    }

    //   operations
    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpGet("inventory/receive")]
    public Task<IActionResult> Receive(int? productId = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        OperationView(new ReceiveStockForm(), productId, warehouseId, cancellationToken);

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("inventory/receive")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Receive(ReceiveStockForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await OperationView(form, form.ProductId, form.WarehouseId, cancellationToken);
        }

        return await Record(
            form,
            () => _inventory.ReceiveAsync(
                new ReceiveStockRequest(form.ProductId, form.WarehouseId, form.Quantity, Reference(form)),
                cancellationToken),
            cancellationToken);
    }

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpGet("inventory/issue")]
    public Task<IActionResult> Issue(int? productId = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        OperationView(new IssueStockForm(), productId, warehouseId, cancellationToken);

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("inventory/issue")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Issue(IssueStockForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await OperationView(form, form.ProductId, form.WarehouseId, cancellationToken);
        }

        return await Record(
            form,
            () => _inventory.IssueAsync(
                new IssueStockRequest(form.ProductId, form.WarehouseId, form.Quantity, Reference(form)),
                cancellationToken),
            cancellationToken);
    }

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpGet("inventory/transfer")]
    public Task<IActionResult> Transfer(int? productId = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        OperationView(new TransferStockForm(), productId, warehouseId, cancellationToken);

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("inventory/transfer")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Transfer(TransferStockForm form, CancellationToken cancellationToken)
    {
        if (form.SourceWarehouseId == form.DestinationWarehouseId)
        {
            ModelState.AddModelError(nameof(form.DestinationWarehouseId), "A transfer needs two different warehouses.");
        }

        if (!ModelState.IsValid)
        {
            return await OperationView(form, form.ProductId, form.SourceWarehouseId, cancellationToken);
        }

        var catalogue = await _catalogue.GetAsync(cancellationToken);

        try
        {
            var result = await _inventory.TransferAsync(
                new TransferStockRequest(
                    form.ProductId,
                    form.SourceWarehouseId,
                    form.DestinationWarehouseId,
                    form.Quantity,
                    Reference(form)),
                cancellationToken);

            TempData.Confirm(
                $"Transferred {Display.Quantity(result.Quantity)} of {catalogue.ProductName(result.ProductId)} from " +
                $"{catalogue.WarehouseName(form.SourceWarehouseId)} to {catalogue.WarehouseName(form.DestinationWarehouseId)}. " +
                $"The source now holds {Display.Quantity(result.Source.Quantity)}.");

            return RedirectToLevels(form.ProductId, form.SourceWarehouseId, result.Source.Id);
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return await OperationView(form, form.ProductId, form.SourceWarehouseId, cancellationToken);
        }
    }

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpGet("inventory/adjust")]
    public Task<IActionResult> Adjust(int? productId = null, int? warehouseId = null, CancellationToken cancellationToken = default) =>
        OperationView(new AdjustStockForm(), productId, warehouseId, cancellationToken);

    [Authorize(Policy = ConsolePolicies.Operate)]
    [HttpPost("inventory/adjust")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Adjust(AdjustStockForm form, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return await OperationView(form, form.ProductId, form.WarehouseId, cancellationToken);
        }

        return await Record(
            form,
            () => _inventory.AdjustAsync(
                new AdjustStockRequest(form.ProductId, form.WarehouseId, form.NewQuantity, Reference(form)),
                cancellationToken),
            cancellationToken);
    }

    //   shared

     
    /// Runs an operation that answers with the stock row it changed, and takes the
    /// clerk to that row. A refusal is put back on the form in the service's words,
    /// with everything that was typed still in place.
   
    private async Task<IActionResult> Record(
        StockOperationForm form,
        Func<Task<InventoryDto>> operation,
        CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.GetAsync(cancellationToken);

        try
        {
            var result = await operation();

            TempData.Confirm(MessageFor(form, catalogue, result));

            return RedirectToLevels(form.ProductId, result.WarehouseId, result.Id);
        }
        catch (ApiException exception) when (exception.IsValidationFailure)
        {
            ModelState.AddModelError(string.Empty, exception.Explain());

            return await OperationView(form, form.ProductId, form.WarehouseId, cancellationToken);
        }
    }

    private async Task<IActionResult> OperationView(
        StockOperationForm form,
        int? productId,
        int? warehouseId,
        CancellationToken cancellationToken)
    {
        var catalogue = await _catalogue.GetAsync(cancellationToken);

        form.Catalog = catalogue;

        if (form.ProductId <= 0 && productId is > 0)
        {
            form.ProductId = productId.Value;
        }

        if (warehouseId is > 0)
        {
            if (form is TransferStockForm transfer)
            {
                if (transfer.SourceWarehouseId <= 0)
                {
                    transfer.SourceWarehouseId = warehouseId.Value;
                }
            }
            else if (form.WarehouseId <= 0)
            {
                form.WarehouseId = warehouseId.Value;
            }
        }

        ViewData["Nav"] = form.Operation;
        ViewData["Section"] = "Operations";
        ViewData["Title"] = form.Title;
        ViewData["Note"] = form.Explanation;

        return View(form.Operation switch
        {
            "receive" => "Receive",
            "issue" => "Issue",
            "transfer" => "Transfer",
            _ => "Adjust"
        }, form);
    }

    ///  The page the operation ends on: the row it changed, for the product and warehouse it touched. 
    private IActionResult RedirectToLevels(int productId, int warehouseId, int focus) =>
        RedirectToAction(nameof(Index), new { productId, warehouseId, focus });

    private static string? Reference(StockOperationForm form) =>
        string.IsNullOrWhiteSpace(form.Reference) ? null : form.Reference.Trim();

   
    /// What the flash says after an operation. It reports the quantity the service
    /// confirmed, not the quantity that was typed: those are the same number only when
    /// the operation succeeded.
   
    private static string MessageFor(StockOperationForm form, CatalogLookup catalogue, InventoryDto result)
    {
        var product = catalogue.ProductName(result.ProductId);
        var warehouse = catalogue.WarehouseName(result.WarehouseId);
        var quantity = Display.Quantity(result.Quantity);

        return form.Operation switch
        {
            "receive" =>
                $"Received {Display.Quantity(form.Quantity)} of {product} into {warehouse}. {warehouse} now holds {quantity}.",
            "issue" =>
                $"Issued {Display.Quantity(form.Quantity)} of {product} out of {warehouse}. {warehouse} now holds {quantity}.",
            _ =>
                $"Recorded the count for {product} in {warehouse}: the ledger now reads {quantity}."
        };
    }

    ///  The service takes an inclusive end, so a day typed as 2026-10-16 means the whole of that day. 
    private static DateTime? EndOf(DateTime? day) => day?.Date.AddDays(1).AddTicks(-1);
}
