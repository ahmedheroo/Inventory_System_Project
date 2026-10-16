using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Products.Application.Dtos;

public sealed record ProductDto(
    int Id,
    string Sku,
    string Name,
    string? Description,
    int? CategoryId,
    string? CategoryName,
    decimal UnitPrice,
    int MinimumStockLevel,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateProductRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Sku { get; init; } = string.Empty;

    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    public int? CategoryId { get; init; }

    [Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; init; }

    [Range(0, 1_000_000)]
    public int MinimumStockLevel { get; init; }
}

public sealed record UpdateProductRequest
{
    [Required]
    [StringLength(200, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(1000)]
    public string? Description { get; init; }

    public int? CategoryId { get; init; }

    [Range(0, 1_000_000_000)]
    public decimal UnitPrice { get; init; }

    [Range(0, 1_000_000)]
    public int MinimumStockLevel { get; init; }
}

public sealed record ChangeProductSkuRequest
{
    [Required]
    [StringLength(50, MinimumLength = 1)]
    public string Sku { get; init; } = string.Empty;
}

/// Search / filter / paging arguments for the product list endpoint
public sealed record ProductQuery
{
    /// Free-text match against SKU or name
    public string? Search { get; init; }

    public int? CategoryId { get; init; }

    /// When false (default) only active products are returned
    public bool IncludeInactive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;

    public ProductQuery Normalize() => this with
    {
        Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
        Page = Page < 1 ? 1 : Page,
        PageSize = PageSize is < 1 or > 100 ? 20 : PageSize
    };
}
