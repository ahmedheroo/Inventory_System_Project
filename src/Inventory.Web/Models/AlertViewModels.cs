using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

public sealed record AlertFilter(
    AlertStatus? Status = AlertStatus.ACTIVE,
    AlertType? Type = null,
    bool? IsRead = null,
    int? WarehouseId = null,
    int? ProductId = null,
    int Page = 1,
    int PageSize = 20);

public sealed class AlertListViewModel
{
    public PagedResult<AlertDto> Page { get; init; } = PagedResult<AlertDto>.Empty(1, 20);

    public AlertFilter Filter { get; init; } = new();

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public Pager Pager { get; init; } = new("/alerts", new Dictionary<string, string?>(), 1, 20, 0);

    /// <summary>The alerts on this page that are still waiting for a person, in the order they were raised.</summary>
    public IReadOnlyList<AlertDto> Unread => Page.Items.Where(alert => !alert.IsRead).ToList();
}
