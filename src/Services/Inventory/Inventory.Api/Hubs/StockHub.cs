using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace InventorySystem.Stock.Api.Hubs;

/// <summary>What a connected client is sent. Naming a hub method on a typed client keeps the wire contract in one place.</summary>
public interface IStockClient
{
    Task StockChanged(StockChangeNotification notification);
}

/// <summary>
/// The live view of stock: one notification per committed operation, to every connected client.
/// There is no subscription state and nothing to catch up on — a client that connects late renders
/// the current state from the REST API and then follows these notifications, which is why the hub is
/// a broadcast and not a feed.
///
/// It is the only endpoint in this service that is not anonymous. A page read is a question; a hub
/// connection is an open door that keeps answering, from any origin that can reach the port — so it
/// takes a token with the <see cref="JwtScopes.StockRead"/> scope. That token now comes from the
/// console's own relay rather than from the browser, which is the point: nobody who merely reaches
/// this port can watch the stock move.
/// </summary>
[Authorize(Policy = JwtPolicies.StockRead)]
public sealed class StockHub : Hub<IStockClient>
{
    /// <summary>Where the hub is mapped. Named once, so the console's configuration, the tests and the docs cannot drift apart.</summary>
    public const string Path = "/hubs/stock";
}
