using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.BuildingBlocks.Contracts.Events;

namespace InventorySystem.Reports.Application.Services;

/// <summary>
/// One stock movement an event describes. <see cref="Quantity"/> is the magnitude
/// of the movement, or the signed delta of an adjustment; <see cref="QuantityAfter"/>
/// is the quantity the event reports for that warehouse after the operation, which
/// is what the current-inventory projection stores.
/// </summary>
public sealed record StockMovementObservation(
    int ProductId,
    int WarehouseId,
    StockTransactionType Type,
    int Quantity,
    int QuantityAfter,
    int MinimumStockLevel);

/// <summary>
/// The one place that decides which movements an inventory event contributes
/// (TASK-057/TASK-060), mirroring the Alert service's reading of the same events:
///
/// * a transfer is two movements and two stock levels — TRANSFER_OUT for the
///   source, TRANSFER_IN for the destination;
/// * an adjustment reports an absolute new quantity, so its movement is the
///   signed delta (old → new);
/// * the other three events are a single movement in the warehouse they name.
/// </summary>
public static class StockEventProjection
{
    public static IReadOnlyList<StockMovementObservation> Observe(IntegrationEvent integrationEvent) =>
        integrationEvent switch
        {
            StockReceivedEvent received =>
            [
                new(
                    received.ProductId,
                    received.WarehouseId,
                    StockTransactionType.RECEIVE,
                    received.Quantity,
                    received.CurrentQuantity,
                    received.MinimumStockLevel)
            ],
            StockIssuedEvent issued =>
            [
                new(
                    issued.ProductId,
                    issued.WarehouseId,
                    StockTransactionType.ISSUE,
                    issued.Quantity,
                    issued.CurrentQuantity,
                    issued.MinimumStockLevel)
            ],
            StockAdjustedEvent adjusted =>
            [
                new(
                    adjusted.ProductId,
                    adjusted.WarehouseId,
                    StockTransactionType.ADJUSTMENT,
                    adjusted.NewQuantity - adjusted.OldQuantity,
                    adjusted.NewQuantity,
                    adjusted.MinimumStockLevel)
            ],
            StockTransferredEvent transferred =>
            [
                new(
                    transferred.ProductId,
                    transferred.SourceWarehouseId,
                    StockTransactionType.TRANSFER_OUT,
                    transferred.Quantity,
                    transferred.SourceCurrentQuantity,
                    transferred.MinimumStockLevel),
                new(
                    transferred.ProductId,
                    transferred.DestinationWarehouseId,
                    StockTransactionType.TRANSFER_IN,
                    transferred.Quantity,
                    transferred.DestinationCurrentQuantity,
                    transferred.MinimumStockLevel)
            ],
            _ => throw new NotSupportedException(
                $"Event '{integrationEvent.EventName}' is not projected by the Reporting service.")
        };
}
