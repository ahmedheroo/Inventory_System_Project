namespace Inventory.Web.Api.Contracts;

///Kind of stock transaction recorded by the Inventory service (serialized as a number)
public enum StockTransactionType
{
    RECEIVE = 0,
    ISSUE = 1,
    TRANSFER_IN = 2,
    TRANSFER_OUT = 3,
    ADJUSTMENT = 4
}

public sealed record InventoryDto(
    int Id,
    int ProductId,
    int WarehouseId,
    int Quantity,
    DateTime UpdatedAt);

public sealed record InventoryTransactionDto(
    int Id,
    int ProductId,
    int WarehouseId,
    StockTransactionType Type,
    int Quantity,
    int SignedQuantity,
    string? Reference,
    DateTime CreatedAt);

public sealed record TransferResultDto(
    int ProductId,
    int Quantity,
    InventoryDto Source,
    InventoryDto Destination);

public sealed record InventoryQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record HistoryQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public StockTransactionType? Type { get; init; }

    public DateTime? From { get; init; }

    public DateTime? To { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record ReceiveStockRequest(int ProductId, int WarehouseId, int Quantity, string? Reference);

public sealed record IssueStockRequest(int ProductId, int WarehouseId, int Quantity, string? Reference);

public sealed record TransferStockRequest(
    int ProductId,
    int SourceWarehouseId,
    int DestinationWarehouseId,
    int Quantity,
    string? Reference);

public sealed record AdjustStockRequest(int ProductId, int WarehouseId, int NewQuantity, string? Reference);
