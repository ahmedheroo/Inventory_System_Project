using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Api;

/// <summary>
/// The four reports and their downloads. The dashboard reads the summary of each
/// report; the report pages read one page of rows; the download action streams the
/// file through unchanged, filters and all.
/// </summary>
public interface IReportingApi
{
    Task<InventoryReport> GetInventoryReportAsync(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<LowStockReport> GetLowStockReportAsync(
        int? productId = null,
        int? warehouseId = null,
        bool includeOutOfStock = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<MovementReport> GetMovementReportAsync(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    Task<ValuationReport> GetValuationReportAsync(
        int? warehouseId = null,
        int? categoryId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default);

    /// <param name="report">One of <c>inventory</c>, <c>low-stock</c>, <c>movement</c>, <c>valuation</c>.</param>
    /// <param name="query">The query string as the browser asked for it, forwarded verbatim.</param>
    Task<DownloadedFile> DownloadAsync(string report, string query, CancellationToken cancellationToken = default);
}

public sealed class ReportingApi : ApiClientBase, IReportingApi
{
    /// <summary>The reports the download endpoint serves; the route is only ever built from this list.</summary>
    private static readonly string[] Downloadable = ["inventory", "low-stock", "movement", "valuation"];

    public ReportingApi(HttpClient httpClient, ILogger<ReportingApi> logger)
        : base(httpClient, logger, "Reporting service")
    {
    }

    public Task<InventoryReport> GetInventoryReportAsync(
        int? productId = null,
        int? warehouseId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GetAsync<InventoryReport>(
            "/api/reports/inventory" + ApiQuery.Build(
                ("productId", productId),
                ("warehouseId", warehouseId),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);

    public Task<LowStockReport> GetLowStockReportAsync(
        int? productId = null,
        int? warehouseId = null,
        bool includeOutOfStock = true,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GetAsync<LowStockReport>(
            "/api/reports/low-stock" + ApiQuery.Build(
                ("productId", productId),
                ("warehouseId", warehouseId),
                ("includeOutOfStock", includeOutOfStock),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);

    public Task<MovementReport> GetMovementReportAsync(
        int? productId = null,
        int? warehouseId = null,
        StockTransactionType? type = null,
        DateTime? from = null,
        DateTime? to = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GetAsync<MovementReport>(
            "/api/reports/movement" + ApiQuery.Build(
                ("productId", productId),
                ("warehouseId", warehouseId),
                ("type", type),
                ("from", from),
                ("to", to),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);

    public Task<ValuationReport> GetValuationReportAsync(
        int? warehouseId = null,
        int? categoryId = null,
        int page = 1,
        int pageSize = 20,
        CancellationToken cancellationToken = default) =>
        GetAsync<ValuationReport>(
            "/api/reports/valuation" + ApiQuery.Build(
                ("warehouseId", warehouseId),
                ("categoryId", categoryId),
                ("page", page),
                ("pageSize", pageSize)),
            cancellationToken);

    public Task<DownloadedFile> DownloadAsync(
        string report,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (!Downloadable.Contains(report, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentOutOfRangeException(nameof(report), report, "Unknown report.");
        }

        return DownloadAsync($"/api/reports/{report.ToLowerInvariant()}/export{query}", cancellationToken);
    }
}
