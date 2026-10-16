using InventorySystem.BuildingBlocks.Contracts;

namespace InventorySystem.Stock.Application.Dtos;

public sealed record InventoryTransactionDto(
    int Id,
    int ProductId,
    int WarehouseId,
    StockTransactionType Type,
    int Quantity,
    int SignedQuantity,
    string? Reference,
    DateTime CreatedAt);

/// Filter / paging arguments for the transaction history
public sealed record HistoryQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public StockTransactionType? Type { get; init; }

    /// Inclusive lower bound on the transaction timestamp.
    public DateTime? From { get; init; }

    /// Inclusive upper bound on the transaction timestamp
    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public HistoryQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 200 ? 20 : PageSize,
        From = From?.ToUniversalTime(),
        To = To?.ToUniversalTime()
    };
}
