namespace Inventory.Web.Models;

/// <summary>
/// The paging strip under a table. It keeps the current filters in every link by
/// construction: the query the page was asked for is the query the links carry,
/// with only <c>page</c> changed — so paging never silently drops a filter.
/// </summary>
public sealed record Pager
{
    public Pager(string path, IReadOnlyDictionary<string, string?> query, int page, int pageSize, int totalCount)
    {
        Path = path;
        Query = query;
        Page = page < 1 ? 1 : page;
        PageSize = pageSize <= 0 ? 1 : pageSize;
        TotalCount = totalCount;

        TotalPages = (int)Math.Ceiling(TotalCount / (double)PageSize);

        if (TotalPages > 0 && Page > TotalPages)
        {
            Page = TotalPages;
        }
    }

    public string Path { get; }

    public IReadOnlyDictionary<string, string?> Query { get; }

    public int Page { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages { get; }

    public bool HasPreviousPage => Page > 1;

    public bool HasNextPage => Page < TotalPages;

    /// <summary>The first row on this page, 1-based — what a person reads as "rows 21–40 of 84".</summary>
    public int FirstRowNumber => TotalCount == 0 ? 0 : (Page - 1) * PageSize + 1;

    public int LastRowNumber => Math.Min(Page * PageSize, TotalCount);

    /// <summary>The URL of a page, carrying every current filter.</summary>
    public string Url(int page)
    {
        var parts = Query
            .Where(entry => !string.Equals(entry.Key, "page", StringComparison.OrdinalIgnoreCase))
            .Where(entry => !string.IsNullOrWhiteSpace(entry.Value))
            .Select(entry => $"{Uri.EscapeDataString(entry.Key)}={Uri.EscapeDataString(entry.Value!)}")
            .ToList();

        parts.Add($"page={page}");

        return $"{Path}?{string.Join("&", parts)}";
    }
}
