using System.Globalization;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

public sealed class ReportsController : Controller
{
    /// <summary>The reports this controller serves, as they appear in a URL.</summary>
    private static readonly string[] Reports = ["inventory", "low-stock", "movement", "valuation"];

    private readonly IReportingApi _reporting;
    private readonly ICatalogLookupService _catalogue;
    private readonly ICatalogApi _catalogApi;

    public ReportsController(IReportingApi reporting, ICatalogLookupService catalogue, ICatalogApi catalogApi)
    {
        _reporting = reporting;
        _catalogue = catalogue;
        _catalogApi = catalogApi;
    }

    [HttpGet("reports")]
    public IActionResult Index() => RedirectToAction(nameof(Inventory));

    // ------------------------------------------------------------------ stock

    [HttpGet("reports/inventory")]
    public async Task<IActionResult> Inventory(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filters = new ReportFilters
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Page = page,
            PageSize = pageSize
        };

        var report = await _reporting.GetInventoryReportAsync(
            productId,
            warehouseId,
            page,
            pageSize,
            cancellationToken);

        return ViewOf(
            "Inventory",
            "Stock on hand",
            "Every stock level the Inventory service holds, with the value it carries and whether it is at or below the product's minimum.",
            new InventoryReportViewModel
            {
                Report = report,
                Filters = filters,
                Catalog = await _catalogue.GetAsync(cancellationToken),
                Pager = PagerOf("/reports/inventory", filters, report.Page, report.PageSize, report.TotalCount)
            });
    }

    [HttpGet("reports/low-stock")]
    public async Task<IActionResult> LowStock(
        int? productId = null,
        int? warehouseId = null,
        bool includeOutOfStock = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filters = new ReportFilters
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            IncludeOutOfStock = includeOutOfStock,
            Page = page,
            PageSize = pageSize
        };

        var report = await _reporting.GetLowStockReportAsync(
            productId,
            warehouseId,
            includeOutOfStock,
            page,
            pageSize,
            cancellationToken);

        return ViewOf(
            "LowStock",
            "Short stock",
            "What is below its minimum right now, with the quantity it takes to get back to it.",
            new LowStockReportViewModel
            {
                Report = report,
                Filters = filters,
                Catalog = await _catalogue.GetAsync(cancellationToken),
                Pager = PagerOf("/reports/low-stock", filters, report.Page, report.PageSize, report.TotalCount)
            });
    }

    // --------------------------------------------------------------- movement

    [HttpGet("reports/movement")]
    public async Task<IActionResult> Movement(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filters = new ReportFilters
        {
            ProductId = productId,
            WarehouseId = warehouseId,
            Type = type,
            From = from,
            To = to,
            Page = page,
            PageSize = pageSize
        };

        var report = await _reporting.GetMovementReportAsync(
            productId,
            warehouseId,
            type,
            from,
            EndOf(to),
            page,
            pageSize,
            cancellationToken);

        return ViewOf(
            "Movement",
            "Movement ledger",
            "Every movement in the period, newest first, with the quantity after each entry. Dates are UTC days and include the day you type.",
            new MovementReportViewModel
            {
                Report = report,
                Filters = filters,
                Catalog = await _catalogue.GetAsync(cancellationToken),
                Pager = PagerOf("/reports/movement", filters, report.Page, report.PageSize, report.TotalCount)
            });
    }

    // -------------------------------------------------------------- valuation

    [HttpGet("reports/valuation")]
    public async Task<IActionResult> Valuation(
        int? warehouseId = null,
        int? categoryId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var filters = new ReportFilters
        {
            WarehouseId = warehouseId,
            CategoryId = categoryId,
            Page = page,
            PageSize = pageSize
        };

        var report = await _reporting.GetValuationReportAsync(warehouseId, categoryId, page, pageSize, cancellationToken);

        return ViewOf(
            "Valuation",
            "Stock valuation",
            "Quantity times the product's unit price, per product and warehouse, with what each warehouse is worth.",
            new ValuationReportViewModel
            {
                Report = report,
                Filters = filters,
                Catalog = await _catalogue.GetAsync(cancellationToken),
                Categories = await _catalogApi.GetCategoriesAsync(cancellationToken),
                Pager = PagerOf("/reports/valuation", filters, report.Page, report.PageSize, report.TotalCount)
            });
    }

    // -------------------------------------------------------------- downloads

    /// <summary>
    /// Streams one report as a file. The query the browser asked for is handed to the
    /// Reporting service unchanged, so the download is the report on screen — every
    /// matching row of it, filters included — and never a differently filtered file.
    /// </summary>
    [HttpGet("reports/{report}/export")]
    public async Task<IActionResult> Export(string report, CancellationToken cancellationToken)
    {
        var format = Request.Query["format"].ToString();

        if (!Reports.Contains(report, StringComparer.OrdinalIgnoreCase) || format is not ("csv" or "xlsx"))
        {
            return BadRequest();
        }

        var file = await _reporting.DownloadAsync(report, Request.QueryString.Value ?? string.Empty, cancellationToken);

        return File(file.Content, file.ContentType, file.FileName);
    }

    // ------------------------------------------------------------------ shared

    private IActionResult ViewOf(string view, string title, string note, object model)
    {
        ViewData["Nav"] = "reports";
        ViewData["Section"] = "Analysis";
        ViewData["Title"] = title;
        ViewData["Note"] = note;

        return View(view, model);
    }

    private static Pager PagerOf(string path, ReportFilters filters, int page, int pageSize, int totalCount)
    {
        var query = filters.ToQuery(includePaging: false);

        // Paging must not quietly resize the page it is paging.
        query["pageSize"] = pageSize.ToString(CultureInfo.InvariantCulture);

        return new Pager(path, query, page, pageSize, totalCount);
    }

    /// <summary>The service takes an inclusive upper bound, so a day typed as 2026-10-16 means the whole of that day.</summary>
    private static DateTime? EndOf(DateTime? day) => day?.Date.AddDays(1).AddTicks(-1);
}
