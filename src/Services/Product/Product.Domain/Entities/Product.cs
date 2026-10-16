using InventorySystem.Products.Domain.Exceptions;

namespace InventorySystem.Products.Domain.Entities;


/// A stockable product. Owns its identity (SKU), pricing and the minimum stock
/// level used by the Alert service for low-stock detection.

public class Product
{
    public const int SkuMaxLength = 50;
    public const int NameMaxLength = 200;

    // Required by EF Core.
    private Product()
    {
    }

    private Product(
        string sku,
        string name,
        string? description,
        int? categoryId,
        decimal unitPrice,
        int minimumStockLevel)
    {
        Sku = sku;
        Name = name;
        Description = description;
        CategoryId = categoryId;
        UnitPrice = unitPrice;
        MinimumStockLevel = minimumStockLevel;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public string Sku { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public int? CategoryId { get; private set; }

    public Category? Category { get; private set; }

    public decimal UnitPrice { get; private set; }

    public int MinimumStockLevel { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Product Create(
        string sku,
        string name,
        string? description,
        int? categoryId,
        decimal unitPrice,
        int minimumStockLevel)
    {
        ValidateSku(sku);
        ValidateName(name);
        ValidateUnitPrice(unitPrice);
        ValidateMinimumStockLevel(minimumStockLevel);

        return new Product(
            NormalizeSku(sku),
            name.Trim(),
            Normalize(description),
            categoryId,
            decimal.Round(unitPrice, 2),
            minimumStockLevel);
    }

    public void Update(
        string name,
        string? description,
        int? categoryId,
        decimal unitPrice,
        int minimumStockLevel)
    {
        ValidateName(name);
        ValidateUnitPrice(unitPrice);
        ValidateMinimumStockLevel(minimumStockLevel);

        Name = name.Trim();
        Description = Normalize(description);
        CategoryId = categoryId;
        UnitPrice = decimal.Round(unitPrice, 2);
        MinimumStockLevel = minimumStockLevel;
        UpdatedAt = DateTime.UtcNow;
    }

    public void ChangeSku(string sku)
    {
        ValidateSku(sku);
        Sku = NormalizeSku(sku);
        UpdatedAt = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = DateTime.UtcNow;
    }

    public void Activate()
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAt = DateTime.UtcNow;
    }

    /// <summary>Low-stock rule from the specification: current quantity &lt;= minimum stock level.</summary>
    public bool IsLowStock(int currentQuantity) => currentQuantity <= MinimumStockLevel;

    public static string NormalizeSku(string sku) => sku.Trim().ToUpperInvariant();

    private static void ValidateSku(string sku)
    {
        if (string.IsNullOrWhiteSpace(sku))
        {
            throw new DomainException("Product SKU is required.");
        }

        if (sku.Trim().Length > SkuMaxLength)
        {
            throw new DomainException($"Product SKU must be at most {SkuMaxLength} characters.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Product name is required.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Product name must be at most {NameMaxLength} characters.");
        }
    }

    private static void ValidateUnitPrice(decimal unitPrice)
    {
        if (unitPrice < 0)
        {
            throw new DomainException("Unit price cannot be negative.");
        }
    }

    private static void ValidateMinimumStockLevel(int minimumStockLevel)
    {
        if (minimumStockLevel < 0)
        {
            throw new DomainException("Minimum stock level cannot be negative.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
