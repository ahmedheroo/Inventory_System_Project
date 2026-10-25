using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Inventory.Web.Api.Contracts;

namespace Inventory.Web.Models;

/// <summary>
/// How values read on screen. Everything is formatted with the invariant culture on
/// purpose: this console is a warehouse record, and a quantity written as "1.234" in
/// one locale and "1,234" in another is a defect waiting to happen.
/// </summary>
public static class Display
{
    public static string Money(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    /// <summary>A decimal as an input value: always a dot, never a thousands separator.</summary>
    public static string Amount(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    public static string Quantity(int value) => value.ToString("N0", CultureInfo.InvariantCulture);

    public static string Signed(int value) =>
        value > 0
            ? "+" + value.ToString("N0", CultureInfo.InvariantCulture)
            : value.ToString("N0", CultureInfo.InvariantCulture);

    /// <summary>Timestamps exactly as the services report them: UTC, with no silent conversion.</summary>
    public static string Timestamp(DateTime value) =>
        value.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Date(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string DateInput(DateTime? value) =>
        value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>The words a person uses for a ledger entry.</summary>
    public static string MovementName(StockTransactionType type) => type switch
    {
        StockTransactionType.RECEIVE => "Received",
        StockTransactionType.ISSUE => "Issued",
        StockTransactionType.TRANSFER_IN => "Transfer in",
        StockTransactionType.TRANSFER_OUT => "Transfer out",
        StockTransactionType.ADJUSTMENT => "Adjusted",
        _ => type.ToString()
    };

    public static string MovementTone(StockTransactionType type) => type switch
    {
        StockTransactionType.RECEIVE or StockTransactionType.TRANSFER_IN => "up",
        StockTransactionType.ISSUE or StockTransactionType.TRANSFER_OUT => "down",
        _ => "neutral"
    };

    public static string AlertName(AlertType type) =>
        type == AlertType.OUT_OF_STOCK ? "Out of stock" : "Low stock";

    public static string AlertTone(AlertType type) =>
        type == AlertType.OUT_OF_STOCK ? "critical" : "attention";

    public static string AlertStatusName(AlertStatus status) =>
        status == AlertStatus.ACTIVE ? "Active" : "Resolved";

    public static string ShortageTone(LowStockStatus status) =>
        status == LowStockStatus.OUT_OF_STOCK ? "critical" : "attention";

    public static string ShortageName(LowStockStatus status) =>
        status == LowStockStatus.OUT_OF_STOCK ? "Out of stock" : "Low stock";

    /// <summary>
    /// How a stock level reads: out, low, or above the minimum its product asks for.
    /// The rule is the alert rule — at or below the minimum — so the screen cannot
    /// disagree with the service that raises the alerts.
    /// </summary>
    public static (string Tone, string Label) StockState(int quantity, int? minimumStockLevel)
    {
        if (minimumStockLevel is null)
        {
            return ("muted", "No minimum set");
        }

        if (quantity <= 0)
        {
            return ("critical", "Out of stock");
        }

        if (quantity <= minimumStockLevel.Value)
        {
            return ("attention", "Below minimum");
        }

        return ("ok", "Above minimum");
    }

    /// <summary>
    /// The fill level of a stock row against its minimum, for the position bars. A row
    /// at or above twice its minimum is drawn full; a row with nothing on hand keeps a
    /// sliver so the bar still reads as "empty" rather than as missing.
    /// </summary>
    public static int FillPercent(int quantity, int minimumStockLevel)
    {
        var target = minimumStockLevel <= 0 ? Math.Max(quantity, 1) : minimumStockLevel * 2;

        return Math.Clamp((int)Math.Round(quantity / (double)target * 100), 2, 100);
    }
}

public sealed record ProductFilter(
    string? Search = null,
    int? CategoryId = null,
    bool IncludeInactive = false,
    int Page = 1,
    int PageSize = 20);

public sealed class ProductListViewModel
{
    public PagedResult<ProductDto> Page { get; init; } = PagedResult<ProductDto>.Empty(1, 20);

    public IReadOnlyList<CategoryDto> Categories { get; init; } = [];

    public ProductFilter Filter { get; init; } = new();

    public Pager Pager { get; init; } = new("/products", new Dictionary<string, string?>(), 1, 20, 0);
}

/// <summary>Creating a product: the sku is chosen here and never chosen again.</summary>
public sealed class NewProductForm
{
    [Required(ErrorMessage = "Enter a sku.")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "A sku is at most 50 characters.")]
    [Display(Name = "Sku")]
    public string Sku { get; init; } = string.Empty;

    [Required(ErrorMessage = "Enter a product name.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "A name is at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000, ErrorMessage = "The description is at most 1000 characters.")]
    [Display(Name = "Description")]
    public string? Description { get; init; }

    [Display(Name = "Category")]
    public int? CategoryId { get; init; }

    [Range(0, 1_000_000_000, ErrorMessage = "A unit price cannot be negative.")]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; init; }

    [Range(0, 1_000_000, ErrorMessage = "A minimum stock level cannot be negative.")]
    [Display(Name = "Minimum stock level")]
    public int MinimumStockLevel { get; init; }

    public IReadOnlyList<CategoryDto> Categories { get; init; } = [];
}

/// <summary>Editing a product: everything but the sku, which has its own change form.</summary>
public sealed class EditProductForm
{
    public int Id { get; init; }

    public string Sku { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    [Required(ErrorMessage = "Enter a product name.")]
    [StringLength(200, MinimumLength = 1, ErrorMessage = "A name is at most 200 characters.")]
    [Display(Name = "Name")]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000, ErrorMessage = "The description is at most 1000 characters.")]
    [Display(Name = "Description")]
    public string? Description { get; init; }

    [Display(Name = "Category")]
    public int? CategoryId { get; init; }

    [Range(0, 1_000_000_000, ErrorMessage = "A unit price cannot be negative.")]
    [Display(Name = "Unit price")]
    public decimal UnitPrice { get; init; }

    [Range(0, 1_000_000, ErrorMessage = "A minimum stock level cannot be negative.")]
    [Display(Name = "Minimum stock level")]
    public int MinimumStockLevel { get; init; }

    public IReadOnlyList<CategoryDto> Categories { get; init; } = [];
}

public sealed class CategoryListViewModel
{
    public IReadOnlyList<CategoryDto> Categories { get; init; } = [];

    public CategoryFormViewModel CreateForm { get; init; } = new();

    /// <summary>Filled when the page was asked to edit one category (<c>?edit={id}</c>).</summary>
    public CategoryFormViewModel? EditForm { get; init; }
}

public sealed class CategoryFormViewModel
{
    public int Id { get; init; }

    [Required(ErrorMessage = "Enter a category name.")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "A name is at most 100 characters.")]
    [Display(Name = "Name")]
    public string Name { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "The description is at most 500 characters.")]
    [Display(Name = "Description")]
    public string? Description { get; init; }
}

public sealed class WarehouseListViewModel
{
    public IReadOnlyList<WarehouseDto> Warehouses { get; init; } = [];

    public bool IncludeInactive { get; init; }

    public NewWarehouseForm CreateForm { get; init; } = new();

    public EditWarehouseForm? EditForm { get; init; }
}

/// <summary>Creating a warehouse: the code is chosen here and never chosen again.</summary>
public sealed class NewWarehouseForm
{
    [Required(ErrorMessage = "Enter a warehouse code.")]
    [StringLength(20, MinimumLength = 1, ErrorMessage = "A code is at most 20 characters.")]
    [Display(Name = "Code")]
    public string Code { get; init; } = string.Empty;

    [Required(ErrorMessage = "Enter a warehouse name.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "A name is at most 150 characters.")]
    [Display(Name = "Name")]
    public string Name { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "The address is at most 500 characters.")]
    [Display(Name = "Address")]
    public string? Address { get; init; }
}

public sealed class EditWarehouseForm
{
    public int Id { get; init; }

    public string Code { get; init; } = string.Empty;

    public bool IsActive { get; init; } = true;

    [Required(ErrorMessage = "Enter a warehouse name.")]
    [StringLength(150, MinimumLength = 1, ErrorMessage = "A name is at most 150 characters.")]
    [Display(Name = "Name")]
    public string Name { get; init; } = string.Empty;

    [StringLength(500, ErrorMessage = "The address is at most 500 characters.")]
    [Display(Name = "Address")]
    public string? Address { get; init; }
}
