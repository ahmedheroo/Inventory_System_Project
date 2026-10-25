using InventorySystem.Stock.Application.Notifications;

namespace InventorySystem.Stock.Application.Abstractions;

/// <summary>
/// Tells whoever is watching that a stock level just changed.
///
/// The call happens after the operation has been committed, so a subscriber that is slow,
/// absent or broken cannot change the outcome of a stock operation: an implementation
/// must not throw. The live view of the system is a convenience on top of the ledger,
/// never a participant in it.
/// </summary>
public interface IStockBroadcaster
{
    Task StockChangedAsync(StockChangeNotification notification, CancellationToken cancellationToken = default);
}
