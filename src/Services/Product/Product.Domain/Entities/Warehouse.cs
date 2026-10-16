using InventorySystem.Products.Domain.Exceptions;

namespace InventorySystem.Products.Domain.Entities;


/// A physical location that holds stock. Inventory references warehouses by id;
/// the Inventory service never reads this table directly.

public class Warehouse
{
    public const int CodeMaxLength = 20;
    public const int NameMaxLength = 150;

    // Required by EF Core.
    private Warehouse()
    {
    }

    private Warehouse(string code, string name, string? address)
    {
        Code = code;
        Name = name;
        Address = address;
        IsActive = true;
        CreatedAt = DateTime.UtcNow;
        UpdatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public string Code { get; private set; } = null!;

    public string Name { get; private set; } = null!;

    public string? Address { get; private set; }

    public bool IsActive { get; private set; }

    public DateTime CreatedAt { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Warehouse Create(string code, string name, string? address)
    {
        ValidateCode(code);
        ValidateName(name);
        return new Warehouse(NormalizeCode(code), name.Trim(), Normalize(address));
    }

    public void Update(string name, string? address)
    {
        ValidateName(name);
        Name = name.Trim();
        Address = Normalize(address);
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

    public static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

    private static void ValidateCode(string code)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new DomainException("Warehouse code is required.");
        }

        if (code.Trim().Length > CodeMaxLength)
        {
            throw new DomainException($"Warehouse code must be at most {CodeMaxLength} characters.");
        }
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new DomainException("Warehouse name is required.");
        }

        if (name.Trim().Length > NameMaxLength)
        {
            throw new DomainException($"Warehouse name must be at most {NameMaxLength} characters.");
        }
    }

    private static string? Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
