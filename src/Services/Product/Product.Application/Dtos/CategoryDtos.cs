using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Products.Application.Dtos;

public sealed record CategoryDto(
    int Id,
    string Name,
    string? Description,
    int ProductCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}

public sealed record UpdateCategoryRequest
{
    [Required]
    [StringLength(100, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Description { get; init; }
}
