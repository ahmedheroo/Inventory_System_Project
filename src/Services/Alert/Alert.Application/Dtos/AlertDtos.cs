using InventorySystem.Alerts.Domain.Entities;

namespace InventorySystem.Alerts.Application.Dtos;

/// <summary>
/// An alert as the API returns it. <c>type</c> and <c>status</c> are the numeric
/// enum values (LOW_STOCK = 0, OUT_OF_STOCK = 1 / ACTIVE = 0, RESOLVED = 1) and
/// keep their names in the documentation.
/// </summary>
public sealed record AlertDto(
    int Id,
    int ProductId,
    int WarehouseId,
    AlertType Type,
    string Message,
    int CurrentQuantity,
    int MinimumStockLevel,
    bool IsRead,
    AlertStatus Status,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? ReadAt,
    DateTime? ResolvedAt);

/// <summary>Filter / paging arguments for the alert list.</summary>
public sealed record AlertQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public AlertType? Type { get; init; }

    /// <summary>Filter on read state (spec: alerts can be marked as read).</summary>
    public bool? IsRead { get; init; }

    /// <summary>Defaults to active alerts, which is what the dashboard shows.</summary>
    public AlertStatus? Status { get; init; } = AlertStatus.ACTIVE;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public AlertQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}
