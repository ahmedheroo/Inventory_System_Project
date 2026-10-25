namespace Inventory.Web.Api.Contracts;

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

public sealed record CategoryDto(
    int Id,
    string Name,
    string? Description,
    int ProductCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record WarehouseDto(
    int Id,
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record ProductQuery
{
    public string? Search { get; init; }

    public int? CategoryId { get; init; }

    public bool IncludeInactive { get; init; }

    public int Page { get; init; } = 1;

    public int PageSize { get; init; } = 20;
}

public sealed record CreateProductRequest(
    string Sku,
    string Name,
    string? Description,
    int? CategoryId,
    decimal UnitPrice,
    int MinimumStockLevel);

public sealed record UpdateProductRequest(
    string Name,
    string? Description,
    int? CategoryId,
    decimal UnitPrice,
    int MinimumStockLevel);

public sealed record CreateCategoryRequest(string Name, string? Description);

public sealed record UpdateCategoryRequest(string Name, string? Description);

public sealed record CreateWarehouseRequest(string Code, string Name, string? Address);

public sealed record UpdateWarehouseRequest(string Name, string? Address);
