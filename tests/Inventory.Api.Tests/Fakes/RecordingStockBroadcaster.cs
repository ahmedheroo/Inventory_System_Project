using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Notifications;

namespace InventorySystem.Stock.Api.Tests.Fakes;

/// <summary>
/// The live broadcast, recorded instead of sent. What a test wants to know is that the
/// notification a client would have received describes the operation that was committed —
/// and that a refused operation produces none.
/// </summary>
public sealed class RecordingStockBroadcaster : IStockBroadcaster
{
    public List<StockChangeNotification> Notifications { get; } = [];

    public Task StockChangedAsync(
        StockChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        Notifications.Add(notification);

        return Task.CompletedTask;
    }
}
