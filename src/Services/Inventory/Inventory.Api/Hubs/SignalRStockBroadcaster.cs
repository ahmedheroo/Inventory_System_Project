using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace InventorySystem.Stock.Api.Hubs;

/// <summary>
/// The SignalR implementation of the broadcast: one <c>StockChanged</c> call to every
/// connected client.
///
/// It never throws. The operation it follows has already been committed and answered, so a
/// failure here — a client that vanished mid-send, a payload that cannot be written, a
/// transport that is no longer there — is logged and dropped. Nothing about a stock change
/// depends on somebody watching it.
/// </summary>
public sealed class SignalRStockBroadcaster : IStockBroadcaster
{
    private readonly IHubContext<StockHub, IStockClient> _hub;
    private readonly ILogger<SignalRStockBroadcaster> _logger;

    public SignalRStockBroadcaster(
        IHubContext<StockHub, IStockClient> hub,
        ILogger<SignalRStockBroadcaster> logger)
    {
        _hub = hub;
        _logger = logger;
    }

    public async Task StockChangedAsync(
        StockChangeNotification notification,
        CancellationToken cancellationToken = default)
    {
        try
        {
            await _hub.Clients.All.StockChanged(notification);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(
                exception,
                "The {Change} of {Quantity} for product {ProductId} in warehouse {WarehouseId} was committed but could not be broadcast to live clients.",
                notification.Change,
                notification.Quantity,
                notification.ProductId,
                notification.WarehouseId);
        }
    }
}
