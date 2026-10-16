using System.ComponentModel.DataAnnotations;

namespace InventorySystem.Products.Application.Dtos;

public sealed record WarehouseDto(
    int Id,
    string Code,
    string Name,
    string? Address,
    bool IsActive,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record CreateWarehouseRequest
{
    [Required]
    [StringLength(20, MinimumLength = 1)]
    public string Code { get; init; } = string.Empty;

    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Address { get; init; }
}

public sealed record UpdateWarehouseRequest
{
    [Required]
    [StringLength(150, MinimumLength = 1)]
    public string Name { get; init; } = string.Empty;

    [StringLength(500)]
    public string? Address { get; init; }
}
