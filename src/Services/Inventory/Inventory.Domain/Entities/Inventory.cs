using InventorySystem.Stock.Domain.Exceptions;

namespace InventorySystem.Stock.Domain.Entities;

 
/// The stock level of one product in one warehouse. This is the aggregate that
/// enforces the specification's business rules:
///
/// * quantity can never become negative (rule 1);
/// * an issue/transfer-out can never exceed the available quantity (rule 2);
/// * every modification is timestamped and recorded as a transaction (rule 3).
///
/// <see cref="UpdatedAt"/> doubles as an optimistic-concurrency token, so two
/// simultaneous operations on the same row cannot both act on stale quantities.
 
public class Inventory
{
    // Required by EF Core.
    private Inventory()
    {
    }

    private Inventory(int productId, int warehouseId, int quantity)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Quantity = quantity;
        UpdatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int WarehouseId { get; private set; }

    public int Quantity { get; private set; }

    public DateTime UpdatedAt { get; private set; }

    public static Inventory Create(int productId, int warehouseId, int initialQuantity = 0)
    {
        if (productId <= 0)
        {
            throw new DomainException("Product id must be positive.");
        }

        if (warehouseId <= 0)
        {
            throw new DomainException("Warehouse id must be positive.");
        }

        if (initialQuantity < 0)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        return new Inventory(productId, warehouseId, initialQuantity);
    }

    /// <summary>Adds stock and returns the new quantity.</summary>
    public int Receive(int quantity) => Increase(quantity, "receive");

    /// <summary>Adds stock arriving from a transfer and returns the new quantity.</summary>
    public int TransferIn(int quantity) => Increase(quantity, "transfer in");

    /// <summary>Removes stock, refusing to exceed the available quantity.</summary>
    public int Issue(int quantity) => Decrease(quantity, "issue");

    /// <summary>Removes stock leaving on a transfer, refusing to exceed what is available.</summary>
    public int TransferOut(int quantity) => Decrease(quantity, "transfer out");

    /// <summary>
    /// Sets the quantity to an absolute value (stock count correction) and
    /// returns the signed change that should be recorded as a transaction.
    /// </summary>
    public int Adjust(int newQuantity)
    {
        if (newQuantity < 0)
        {
            throw new DomainException("Quantity cannot be negative.");
        }

        var delta = newQuantity - Quantity;
        Quantity = newQuantity;
        Touch();

        return delta;
    }

    private int Increase(int quantity, string operation)
    {
        EnsurePositive(quantity, operation);
        Quantity += quantity;
        Touch();

        return Quantity;
    }

    private int Decrease(int quantity, string operation)
    {
        EnsurePositive(quantity, operation);

        if (quantity > Quantity)
        {
            throw new DomainException(
                $"Cannot {operation} {quantity} unit(s): only {Quantity} available for product {ProductId} in warehouse {WarehouseId}.");
        }

        Quantity -= quantity;
        Touch();

        return Quantity;
    }

    private static void EnsurePositive(int quantity, string operation)
    {
        if (quantity <= 0)
        {
            throw new DomainException($"The quantity to {operation} must be greater than zero.");
        }
    }

    private void Touch() => UpdatedAt = DateTime.UtcNow;
}
