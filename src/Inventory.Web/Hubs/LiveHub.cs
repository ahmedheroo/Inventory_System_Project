using Inventory.Web.Api.Contracts;
using Inventory.Web.Relay;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Inventory.Web.Hubs;

/// <summary>
/// What a console page is sent. Both methods are named on this interface so the browser's script and
/// the server cannot drift apart without a name changing in one place.
/// </summary>
public interface ILiveClient
{
    /// <summary>A movement was committed, and this console heard about it.</summary>
    Task StockChanged(LiveStockChange change);

    /// <summary>Whether this console is following the Inventory service right now, said once on connect.</summary>
    Task RelayState(RelayStateMessage state);
}

/// <summary>Whether the console's relay is currently following the live stock view.</summary>
public sealed record RelayStateMessage(bool Following, DateTimeOffset? Since);

[Authorize]
public sealed class LiveHub : Hub<ILiveClient>
{
    /// <summary>Where the hub is mapped, named once for the layout, the script and the tests.</summary>
    public const string Path = "/hubs/live";

    /// <summary>The method a page listens for, and the name the Inventory service broadcasts under.</summary>
    public const string StockChangedMethod = nameof(ILiveClient.StockChanged);

    /// <summary>The method that reports the console's own link to the Inventory service.</summary>
    public const string RelayStateMethod = nameof(ILiveClient.RelayState);

    private readonly LiveRelayState _relay;

    public LiveHub(LiveRelayState relay)
    {
        _relay = relay;
    }

    public override async Task OnConnectedAsync()
    {
        // Told once, on connect: a page that is watching a console which is not following anything
        // should say so, rather than show "live updates on" over a screen that will never move.
        await Clients.Caller.RelayState(new RelayStateMessage(_relay.Following, _relay.Since));

        await base.OnConnectedAsync();
    }
}
