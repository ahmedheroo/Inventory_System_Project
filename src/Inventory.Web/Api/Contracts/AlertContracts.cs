namespace Inventory.Web.Api.Contracts;

public enum AlertType
{
    LOW_STOCK = 0,
    OUT_OF_STOCK = 1
}

public enum AlertStatus
{
    ACTIVE = 0,
    RESOLVED = 1
}

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

public sealed record AlertQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public AlertType? Type { get; init; }

    public bool? IsRead { get; init; }

    public AlertStatus? Status { get; init; } = AlertStatus.ACTIVE;

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}
