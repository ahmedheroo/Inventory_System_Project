using System.Diagnostics;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Models;
using Inventory.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.Web.Controllers;

/// <summary>
/// The overview — the screen a manager opens first — and the two screens that answer
/// with a status instead of a page.
/// </summary>
public sealed class HomeController : Controller
{
    /// <summary>How many short rows the board is read from. Beyond this the page says so rather than pretending it saw everything.</summary>
    private const int ShortageScan = 100;

    private const int RowsPerWarehouse = 3;

    private const int ShortStockRows = 8;

    private const int RecentMovementRows = 8;

    private const int AlertRows = 6;

    private readonly IReportingApi _reporting;
    private readonly IAlertsApi _alerts;
    private readonly ICatalogLookupService _catalogue;
    private readonly ILogger<HomeController> _logger;

    public HomeController(
        IReportingApi reporting,
        IAlertsApi alerts,
        ICatalogLookupService catalogue,
        ILogger<HomeController> logger)
    {
        _reporting = reporting;
        _alerts = alerts;
        _catalogue = catalogue;
        _logger = logger;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var viewModel = await DashboardAsync(cancellationToken);

        ViewData["Nav"] = "overview";
        ViewData["Section"] = "Dashboard";
        ViewData["Note"] = "Stock position per warehouse, what is short, and the last movements recorded by the services.";

        return View(viewModel);
    }

    /// <summary>
    /// The overview's work surface on its own: what the live client re-reads when the hub says
    /// something moved. It is the same model and the same partial the page is built from, so a
    /// live refresh cannot show a different screen from a reload.
    /// </summary>
    [HttpGet("/live/overview")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> LiveOverview(CancellationToken cancellationToken) =>
        PartialView("_Overview", await DashboardAsync(cancellationToken));

    /// <summary>
    /// Reads the overview from the four services. Every read is independent, so they go out
    /// together: the screen waits for the slowest service once, not four times over.
    /// </summary>
    private async Task<DashboardViewModel> DashboardAsync(CancellationToken cancellationToken)
    {
        var catalogueTask = _catalogue.GetAsync(cancellationToken);
        var valueTask = _reporting.GetValuationReportAsync(page: 1, pageSize: 1, cancellationToken: cancellationToken);
        var shortageTask = _reporting.GetLowStockReportAsync(page: 1, pageSize: ShortageScan, cancellationToken: cancellationToken);
        var movementTask = _reporting.GetMovementReportAsync(page: 1, pageSize: RecentMovementRows, cancellationToken: cancellationToken);
        var alertTask = _alerts.GetAlertsAsync(
            new AlertQuery { Status = AlertStatus.ACTIVE, Page = 1, PageSize = AlertRows },
            cancellationToken);

        await Task.WhenAll(catalogueTask, valueTask, shortageTask, movementTask, alertTask);

        var catalogue = await catalogueTask;
        var value = await valueTask;
        var shortages = await shortageTask;
        var movements = await movementTask;
        var alerts = await alertTask;

        var totals = value.ByWarehouse.ToDictionary(total => total.WarehouseId);
        var shortagesByWarehouse = shortages.Items
            .GroupBy(row => row.WarehouseId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<LowStockReportRow>)group
                    .OrderBy(row => row.Status == LowStockStatus.OUT_OF_STOCK ? 0 : 1)
                    .ThenBy(row => row.Quantity - row.MinimumStockLevel)
                    .Take(RowsPerWarehouse)
                    .ToList());

        var positions = catalogue.Warehouses.Values
            .OrderBy(warehouse => warehouse.Code)
            .Select(warehouse => new WarehousePosition(
                warehouse,
                totals.GetValueOrDefault(warehouse.Id),
                shortagesByWarehouse.GetValueOrDefault(warehouse.Id) ?? []))
            .ToList();

        var board = new DashboardViewModel
        {
            Value = value.Summary,
            Shortages = shortages.Summary,
            Positions = positions,
            ShortStock = shortages.Items
                .OrderBy(row => row.Status == LowStockStatus.OUT_OF_STOCK ? 0 : 1)
                .ThenBy(row => row.Quantity - row.MinimumStockLevel)
                .Take(ShortStockRows)
                .ToList(),
            RecentMovements = movements.Items,
            OpenAlerts = alerts.Items,
            OpenAlertCount = alerts.TotalCount,
            Catalog = catalogue
        };

        if (shortages.TotalCount > shortages.Items.Count)
        {
            _logger.LogInformation(
                "The board read {Scanned} of {Total} short stock rows.",
                shortages.Items.Count,
                shortages.TotalCount);
        }

        return board;
    }

    /// <summary>What the console answers to a status it raised itself (a wrong address, a record that is gone).</summary>
    /// <remarks>
    /// Anonymous on purpose. This page is re-executed *after* a status was raised, so a 401 that sent
    /// a visitor here would otherwise be raised again by this page — and the console is the only place
    /// that can name the code without contradicting itself.
    ///
    /// Every method, not just GET: the re-execution keeps the method of the request that failed, so a
    /// form post that was refused (a stale page, a missing token) arrives here as a POST. A GET-only
    /// page would not match it, and the refusal would be answered with the sign-in screen instead of
    /// the reason.
    /// </remarks>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Status(int code = 404)
    {
        var model = StatusViewModel.For(code);

        ViewData["Nav"] = string.Empty;
        ViewData["Section"] = "Console";
        ViewData["Title"] = model.Headline;

        Response.StatusCode = model.Code;

        return View(model);
    }

    /// <summary>The last word when a screen itself failed. Anonymous, and every method, for the same reasons as <see cref="Status"/>.</summary>
    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        ViewData["Nav"] = string.Empty;
        ViewData["Section"] = "Dashboard";
        ViewData["Title"] = "Something went wrong";

        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
