using InventorySystem.Products.Domain.Exceptions;

namespace InventorySystem.Products.Domain.Entities;


/// A product category. Products may optionally belong to one category.

public class Category
{
    public const int NameMaxLength = 100;

    // Required by EF Core.
    private Category()
    {
    }

    private Category(string name, string? description)
    {
        Name = name;
        Description = description;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public string Name { get; private set; } = null!;

    public string? Description { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public ICollection<Product> Products { get; private set; } = new List<Product>();

    public static Category Create(string name, string? description)
    {
        Validate(name);
        return new Category(name.Trim(), Normalize(description));
    }

    public void Update(string name, string? description)
    {
        Validate(name);
        Name = name.Trim();
        Description = Normalize(description);
        UpdatedAt = DateTime.UtcNow;
    }

    private static void Validate(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Category name is required.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Category name must be at most {NameMaxLength} characters.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
