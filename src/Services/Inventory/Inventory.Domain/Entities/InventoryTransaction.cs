using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.Stock.Domain.Exceptions;

namespace InventorySystem.Stock.Domain.Entities;

/// <summary>
/// An immutable stock-ledger entry. Every stock modification appends one of
/// these (spec rule 3); a transfer appends two — TRANSFER_OUT and TRANSFER_IN
/// (spec rule 4).
///
/// <see cref="Quantity"/> is the magnitude of the movement for
/// RECEIVE / ISSUE / TRANSFER_IN / TRANSFER_OUT, and the signed delta for
/// ADJUSTMENT (which may be negative). <see cref="SignedQuantity"/> normalises
/// both shapes into a signed value so reporting can simply sum it.
/// </summary>
public class InventoryTransaction
{
    public const int ReferenceMaxLength = 200;

    // Required by EF Core.
    private InventoryTransaction()
    {
    }

    private InventoryTransaction(
        int productId,
        int warehouseId,
        StockTransactionType type,
        int quantity,
        string? reference)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Type = type;
        Quantity = quantity;
        Reference = reference;
        CreatedAt = DateTime.UtcNow;
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int WarehouseId { get; private set; }

    public StockTransactionType Type { get; private set; }

    public int Quantity { get; private set; }

    public string? Reference { get; private set; }

    public DateTime CreatedAt { get; private set; }

    /// <summary>The movement expressed as a signed number (+in / -out).</summary>
    public int SignedQuantity => Type switch
    {
        StockTransactionType.RECEIVE => Quantity,
        StockTransactionType.TRANSFER_IN => Quantity,
        StockTransactionType.ISSUE => -Quantity,
        StockTransactionType.TRANSFER_OUT => -Quantity,
        StockTransactionType.ADJUSTMENT => Quantity,
        _ => 0
    };

    public static InventoryTransaction Create(
        int productId,
        int warehouseId,
        StockTransactionType type,
        int quantity,
        string? reference = null)
    {
        if (productId <= 0)
        {
            throw new DomainException("Product id must be positive.");
        }

        if (warehouseId <= 0)
        {
            throw new DomainException("Warehouse id must be positive.");
        }

        if (type == StockTransactionType.ADJUSTMENT)
        {
            if (quantity == 0)
            {
                throw new DomainException("An adjustment transaction must record a non-zero change.");
            }
        }
        else if (quantity <= 0)
        {
            throw new DomainException($"A {type} transaction quantity must be greater than zero.");
        }

        if (reference is { Length: > ReferenceMaxLength })
        {
            throw new DomainException($"Transaction reference must be at most {ReferenceMaxLength} characters.");
        }

        return new InventoryTransaction(
            productId,
            warehouseId,
            type,
            quantity,
            string.IsNullOrWhiteSpace(reference) ? null : reference.Trim());
    }
}
