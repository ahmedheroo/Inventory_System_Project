using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Stock.Application.Dtos;


/// Stock level of one product in one warehouse. Product details (name, minimum
/// stock level) are deliberately absent: they belong to the Product service and
/// are joined by the caller or by the Reporting service's read model.

public sealed record InventoryDto(
    int Id,
    int ProductId,
    int WarehouseId,
    int Quantity,
    DateTime UpdatedAt);

public sealed record ReceiveStockRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    /// Number of units received; must be greater than zero.
    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed record IssueStockRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    /// Number of units issued; must be greater than zero and no more than the available quantity.
    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed record TransferStockRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int SourceWarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int DestinationWarehouseId { get; init; }

    [Range(1, int.MaxValue)]
    public int Quantity { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}

public sealed record AdjustStockRequest
{
    [Range(1, int.MaxValue)]
    public int ProductId { get; init; }

    [Range(1, int.MaxValue)]
    public int WarehouseId { get; init; }

    /// The counted quantity; the stock level is set to this value.
    [Range(0, int.MaxValue)]
    public int NewQuantity { get; init; }

    [StringLength(200)]
    public string? Reference { get; init; }
}

///Result of a transfer: both affected stock levels.
public sealed record TransferResultDto(
    int ProductId,
    int Quantity,
    InventoryDto Source,
    InventoryDto Destination);

/// Filter / paging arguments for the stock list.
public sealed record InventoryQuery
{
    public int? ProductId { get; init; }

    public int? WarehouseId { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public InventoryQuery Normalize() => this with
    {
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}
