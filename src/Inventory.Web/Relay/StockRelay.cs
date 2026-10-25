using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Hubs;
using Inventory.Web.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;

namespace Inventory.Web.Relay;

/// <summary>Hands one movement to the pages that are open on this console.</summary>
public interface ILiveBroadcaster
{
    Task StockChangedAsync(LiveStockChange change, CancellationToken cancellationToken);
}

/// <summary>The broadcaster a running console uses: every page connected to this console's own hub.</summary>
public sealed class HubLiveBroadcaster : ILiveBroadcaster
{
    private readonly IHubContext<LiveHub, ILiveClient> _hub;

    public HubLiveBroadcaster(IHubContext<LiveHub, ILiveClient> hub)
    {
        _hub = hub;
    }

    public Task StockChangedAsync(LiveStockChange change, CancellationToken cancellationToken) =>
        _hub.Clients.All.StockChanged(change);
}

/// <summary>
/// Whether the console is following the Inventory service's live view, and since when. Read by the
/// hub when a page connects: a browser should be told when the console it is watching is not
/// currently hearing anything.
/// </summary>
public sealed class LiveRelayState
{
    private readonly object _gate = new();

    private bool _following;
    private DateTimeOffset? _since;

    public bool Following
    {
        get
        {
            lock (_gate)
            {
                return _following;
            }
        }
    }

    public DateTimeOffset? Since
    {
        get
        {
            lock (_gate)
            {
                return _since;
            }
        }
    }

    public void FollowingSince(DateTimeOffset at)
    {
        lock (_gate)
        {
            _following = true;
            _since = at;
        }
    }

    public void Lost(DateTimeOffset at)
    {
        lock (_gate)
        {
            _following = false;
            _since = at;
        }
    }
}

/// <summary>
/// The console's half of the live view: one connection to the Inventory service's hub, made with the
/// console's own token, and everything it hears handed straight to this console's own hub.
///
/// It is a background service because the connection must outlive a request — a page comes and goes,
/// and the notifications it needs arrive between its requests. If the Inventory service is not there,
/// this keeps trying and says so; nothing else in the console fails because a live view is down, since
/// every screen is still rendered from the REST APIs.
/// </summary>
public sealed class StockRelay : BackgroundService
{
    /// <summary>Waits between attempts, growing to a limit: a service that is down stays down for a while.</summary>
    private static readonly TimeSpan[] Backoff =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    /// <summary>How the client retries a connection it has already made.</summary>
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10)
    ];

    private readonly ServiceApiOptions _services;
    private readonly IServiceTokenProvider _tokens;
    private readonly ILiveBroadcaster _broadcaster;
    private readonly LiveRelayState _state;
    private readonly TimeProvider _clock;
    private readonly ILogger<StockRelay> _logger;

    public StockRelay(
        ServiceApiOptions services,
        IServiceTokenProvider tokens,
        ILiveBroadcaster broadcaster,
        LiveRelayState state,
        TimeProvider clock,
        ILogger<StockRelay> logger)
    {
        _services = services;
        _tokens = tokens;
        _broadcaster = broadcaster;
        _state = state;
        _clock = clock;
        _logger = logger;
    }

    /// <summary>
    /// Where the Inventory service is followed. Configured rather than derived, because the address
    /// this server can reach is not always the address a browser would use.
    /// </summary>
    public Uri HubUrl => _services.InventoryHub;

    /// <summary>
    /// One movement, on its way to the pages. The hub's callback is exactly this method, so what a test
    /// drives here is what a running console does.
    /// </summary>
    public Task RelayAsync(LiveStockChange change, CancellationToken cancellationToken = default) =>
        _broadcaster.StockChangedAsync(change, cancellationToken);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var attempt = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            if (!await WaitAsync(Backoff[Math.Min(attempt, Backoff.Length - 1)], stoppingToken))
            {
                break;
            }

            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            await using var connection = Build(closed);

            try
            {
                await connection.StartAsync(stoppingToken);

                attempt = 0;
                _state.FollowingSince(_clock.GetUtcNow());

                _logger.LogInformation("Following the live stock view at {Hub}.", HubUrl);

                await closed.Task.WaitAsync(stoppingToken);

                _logger.LogWarning("The live stock view connection closed; the console will try again.");
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Logged, never fatal: a console without its Inventory service still reads, and the
                // pages say what they cannot do rather than pretending the stock is not moving.
                _logger.LogWarning(
                    exception,
                    "The console could not follow the live stock view at {Hub}: {Message}",
                    HubUrl,
                    exception.Message);
            }

            attempt++;
            _state.Lost(_clock.GetUtcNow());
        }
    }

    private HubConnection Build(TaskCompletionSource closed)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(HubUrl, options =>
            {
                // Asked for per attempt, so a reconnect after the token expired presents a new one
                // rather than the refusal that made it reconnect in the first place.
                options.AccessTokenProvider = async () => await _tokens.AccessTokenAsync();
            })
            .WithAutomaticReconnect(ReconnectDelays)
            .Build();

        connection.On<LiveStockChange>(LiveHub.StockChangedMethod, change => RelayAsync(change));

        connection.Reconnecting += _ =>
        {
            _state.Lost(_clock.GetUtcNow());
            _logger.LogInformation("The live stock view is paused; reconnecting.");

            return Task.CompletedTask;
        };

        connection.Reconnected += _ =>
        {
            _state.FollowingSince(_clock.GetUtcNow());
            _logger.LogInformation("The live stock view is following again.");

            return Task.CompletedTask;
        };

        connection.Closed += _ =>
        {
            closed.TrySetResult();

            return Task.CompletedTask;
        };

        return connection;
    }

    private static async Task<bool> WaitAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return !cancellationToken.IsCancellationRequested;
        }

        try
        {
            await Task.Delay(delay, cancellationToken);

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
