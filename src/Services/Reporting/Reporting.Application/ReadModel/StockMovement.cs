using InventorySystem.BuildingBlocks.Contracts;

namespace InventorySystem.Reports.Application.ReadModel;

/// <summary>
/// One entry of the Reporting service's movement ledger (the "stock movement
/// report", TASK-060). Every consumed event appends its movements here — a
/// transfer appends two, TRANSFER_OUT for the source and TRANSFER_IN for the
/// destination — so the ledger is append-only history while
/// <see cref="StockLevelSnapshot"/> holds the current quantity.
///
/// <see cref="Quantity"/> is the magnitude for RECEIVE / ISSUE / TRANSFER_IN /
/// TRANSFER_OUT, and the signed delta for ADJUSTMENT (which can go either way).
/// <see cref="SignedQuantity"/> normalises both into a signed number so sums are
/// meaningful, exactly as the Inventory service's ledger does for its own
/// transactions.
/// </summary>
public class StockMovement
{
    // Required by EF Core.
    private StockMovement()
    {
    }

    private StockMovement(
        int productId,
        int warehouseId,
        StockTransactionType type,
        int quantity,
        int quantityAfter,
        Guid eventId,
        DateTime occurredAt)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Type = type;
        Quantity = quantity;
        QuantityAfter = quantityAfter;
        EventId = eventId;
        OccurredAt = occurredAt;
    }

    public int Id { get; private set; }

    public int ProductId { get; private set; }

    public int WarehouseId { get; private set; }

    public StockTransactionType Type { get; private set; }

    /// <summary>Magnitude of the movement, or the signed change of an adjustment.</summary>
    public int Quantity { get; private set; }

    /// <summary>Quantity on hand in this warehouse right after the movement.</summary>
    public int QuantityAfter { get; private set; }

    /// <summary>The event this movement came from (traceability and de-duplication).</summary>
    public Guid EventId { get; private set; }

    /// <summary>When the stock operation happened (the event's timestamp).</summary>
    public DateTime OccurredAt { get; private set; }

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

    public static StockMovement Record(
        int productId,
        int warehouseId,
        StockTransactionType type,
        int quantity,
        int quantityAfter,
        Guid eventId,
        DateTime occurredAt)
    {
        if (productId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(productId), productId, "A movement must reference a product.");
        }

        if (warehouseId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(warehouseId), warehouseId, "A movement must reference a warehouse.");
        }

        if (quantityAfter < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantityAfter), quantityAfter, "Quantity cannot be negative.");
        }

        if (eventId == Guid.Empty)
        {
            throw new ArgumentOutOfRangeException(nameof(eventId), eventId, "A movement must reference the event it came from.");
        }

        if (type == StockTransactionType.ADJUSTMENT)
        {
            if (quantity == 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(quantity),
                    quantity,
                    "An adjustment movement must record a non-zero change.");
            }
        }
        else if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(quantity),
                quantity,
                $"A {type} movement quantity must be greater than zero.");
        }

        return new StockMovement(productId, warehouseId, type, quantity, quantityAfter, eventId, occurredAt);
    }
}
