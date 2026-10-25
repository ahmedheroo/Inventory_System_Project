using System.Globalization;
using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>
/// The filters of a report page. One object builds both the links the page renders
/// (paging keeps them, downloads keep them) and the call the service receives, so a
/// CSV can never contain a different filter than the table it was taken from.
/// </summary>
public sealed record ReportFilters
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public int? CategoryId { get; init; }

    public StockTransactionType? Type { get; init; }

    /// <summary>The first day the report includes, as typed on the page.</summary>
    public DateTime? From { get; init; }

    /// <summary>The last day the report includes. The service takes an inclusive upper bound, so the day is widened to its last tick before the call.</summary>
    public DateTime? To { get; init; }

    /// <summary>Only the low-stock report reads it; the others ignore it.</summary>
    public bool IncludeOutOfStock { get; init; } = true;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public static ReportFilters Empty { get; } = new();

    /// <summary>The filters as a query, without paging when the call is a download.</summary>
    public Dictionary<string, string?> ToQuery(bool includePaging = true)
    {
        var query = new Dictionary<string, string?>();

        if (ProductId is not null)
        {
            query["productId"] = Number(ProductId.Value);
        }

        if (WarehouseId is not null)
        {
            query["warehouseId"] = Number(WarehouseId.Value);
        }

        if (CategoryId is not null)
        {
            query["categoryId"] = Number(CategoryId.Value);
        }

        if (Type is not null)
        {
            query["type"] = Type.Value.ToString();
        }

        if (From is not null)
        {
            query["from"] = From.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (To is not null)
        {
            query["to"] = To.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        }

        if (!IncludeOutOfStock)
        {
            query["includeOutOfStock"] = "false";
        }

        if (includePaging)
        {
            query["page"] = Number(Page);
            query["pageSize"] = Number(PageSize);
        }

        return query;
    }

    /// <summary>The link to one of the report's own downloads, filters included.</summary>
    public string DownloadUrl(string report, string format) =>
        "/reports/" + report + "/export" + QueryString(ToQuery(includePaging: false), ("format", format));

    private static string QueryString(Dictionary<string, string?> query, params (string Name, string Value)[] extra)
    {
        var parts = query
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value!)}")
            .Concat(extra.Select(pair => $"{Uri.EscapeDataString(pair.Name)}={Uri.EscapeDataString(pair.Value)}"))
            .ToList();

        return parts.Count == 0 ? string.Empty : "?" + string.Join("&", parts);
    }

    private static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Every filter as the page shows it, for the "filters in force" line above a table.</summary>
    public IReadOnlyList<string> Describe(CatalogLookup catalog, IReadOnlyList<CategoryDto>? categories = null)
    {
        var parts = new List<string>();

        if (ProductId is not null)
        {
            parts.Add($"product {catalog.Sku(ProductId.Value)}");
        }

        if (WarehouseId is not null)
        {
            parts.Add($"warehouse {catalog.WarehouseName(WarehouseId.Value)}");
        }

        if (CategoryId is not null && categories is not null)
        {
            parts.Add($"category {categories.FirstOrDefault(category => category.Id == CategoryId)?.Name ?? $"#{CategoryId}"}");
        }

        if (Type is not null)
        {
            parts.Add($"{Display.MovementName(Type.Value).ToLowerInvariant()} movements");
        }

        if (From is not null)
        {
            parts.Add($"from {Display.Date(From.Value)}");
        }

        if (To is not null)
        {
            parts.Add($"to {Display.Date(To.Value)}");
        }

        if (!IncludeOutOfStock)
        {
            parts.Add("excluding out of stock");
        }

        return parts;
    }
}

public sealed class InventoryReportViewModel
{
    public InventoryReport Report { get; init; } = new([], new(0, 0, 0, 0, 0m, 0, 0), 1, 20, 0);

    public ReportFilters Filters { get; init; } = ReportFilters.Empty;

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public Pager Pager { get; init; } = new("/reports/inventory", new Dictionary<string, string?>(), 1, 20, 0);
}

public sealed class LowStockReportViewModel
{
    public LowStockReport Report { get; init; } = new([], new(0, 0, 0, 0, 0), 1, 20, 0);

    public ReportFilters Filters { get; init; } = ReportFilters.Empty;

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public Pager Pager { get; init; } = new("/reports/low-stock", new Dictionary<string, string?>(), 1, 20, 0);
}

public sealed class MovementReportViewModel
{
    public MovementReport Report { get; init; } = new([], new(0, 0, 0, 0, 0, 0, 0), 1, 20, 0);

    public ReportFilters Filters { get; init; } = ReportFilters.Empty;

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public Pager Pager { get; init; } = new("/reports/movement", new Dictionary<string, string?>(), 1, 20, 0);
}

public sealed class ValuationReportViewModel
{
    public ValuationReport Report { get; init; } = new([], [], new(0, 0, 0, 0, 0m), 1, 20, 0);

    public ReportFilters Filters { get; init; } = ReportFilters.Empty;

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public IReadOnlyList<CategoryDto> Categories { get; init; } = [];

    public Pager Pager { get; init; } = new("/reports/valuation", new Dictionary<string, string?>(), 1, 20, 0);
}
