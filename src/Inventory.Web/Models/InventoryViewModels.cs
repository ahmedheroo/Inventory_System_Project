using System.ComponentModel.DataAnnotations;
using Inventory.Web.Api.Contracts;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Inventory.Web.Models;

public sealed record StockFilter(int? ProductId = null, int? WarehouseId = null, int Page = 1, int PageSize = 20);

public sealed class StockListViewModel
{
    public PagedResult<InventoryDto> Page { get; init; } = PagedResult<InventoryDto>.Empty(1, 20);

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public StockFilter Filter { get; init; } = new();

    public Pager Pager { get; init; } = new("/inventory", new Dictionary<string, string?>(), 1, 20, 0);

    /// <summary>The stock the filters selected, in units — the number a manager checks against the ledger.</summary>
    public int TotalQuantity => Page.Items.Sum(row => row.Quantity);

    /// <summary>The row a stock operation just changed, so the page can point at it.</summary>
    public int? Focus { get; init; }
}

public sealed record HistoryFilter(
    int? ProductId = null,
    int? WarehouseId = null,
    StockTransactionType? Type = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20);

public sealed class HistoryListViewModel
{
    public PagedResult<InventoryTransactionDto> Page { get; init; } = PagedResult<InventoryTransactionDto>.Empty(1, 20);

    public CatalogLookup Catalog { get; init; } = CatalogLookup.Empty;

    public HistoryFilter Filter { get; init; } = new();

    public Pager Pager { get; init; } = new("/inventory/history", new Dictionary<string, string?>(), 1, 20, 0);
}

/// <summary>
/// What the four stock operation pages share: the product and warehouse pickers and
/// the reference a clerk may type. The pickers are filled by the controller on every
/// render (including the re-render after a refused operation), so the form never
/// loses the choice that was made.
/// </summary>
public abstract class StockOperationForm
{
    protected StockOperationForm(string operation, string title, string explanation, string submit)
    {
        Operation = operation;
        Title = title;
        Explanation = explanation;
        Submit = submit;
    }

    [Range(1, int.MaxValue, ErrorMessage = "Choose a product.")]
    [Display(Name = "Product")]
    public int ProductId { get; set; }

    [Display(Name = "Warehouse")]
    public int WarehouseId { get; set; }

    [Range(0, int.MaxValue, ErrorMessage = "A quantity cannot be negative.")]
    [Display(Name = "Quantity")]
    public int Quantity { get; set; } = 1;

    [StringLength(200, ErrorMessage = "A reference is at most 200 characters.")]
    [Display(Name = "Reference")]
    public string? Reference { get; set; }

    /// <summary>Filled by the controller on every render; never posted.</summary>
    [BindNever]
    public CatalogLookup Catalog { get; set; } = CatalogLookup.Empty;

    public string Operation { get; }

    public string Title { get; }

    public string Explanation { get; }

    public string Submit { get; }
}

public sealed class ReceiveStockForm : StockOperationForm
{
    public ReceiveStockForm()
        : base(
            "receive",
            "Receive stock",
            "Books incoming units into a warehouse. The service records a Received entry with a positive quantity and raises a stock received event.",
            "Receive stock")
    {
    }
}

public sealed class IssueStockForm : StockOperationForm
{
    public IssueStockForm()
        : base(
            "issue",
            "Issue stock",
            "Books units out for dispatch, production or write-off. The service refuses an issue that would take the recorded level below zero.",
            "Issue stock")
    {
    }
}

public sealed class TransferStockForm : StockOperationForm
{
    public TransferStockForm()
        : base(
            "transfer",
            "Transfer stock",
            "Moves units between two warehouses as one operation. The service writes the matching transfer out and transfer in entries in a single transaction.",
            "Transfer stock")
    {
    }

    [Display(Name = "From warehouse")]
    public int SourceWarehouseId { get; set; }

    [Display(Name = "To warehouse")]
    public int DestinationWarehouseId { get; set; }
}

public sealed class AdjustStockForm : StockOperationForm
{
    public AdjustStockForm()
        : base(
            "adjust",
            "Adjust stock",
            "Corrects the recorded level after a physical count. The service writes an Adjusted entry holding the difference between the count and the ledger.",
            "Record adjustment")
    {
    }

    /// <summary>The counted quantity; the recorded stock level is set to it.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "A counted quantity cannot be negative.")]
    [Display(Name = "Counted quantity")]
    public int NewQuantity { get; set; }
}
