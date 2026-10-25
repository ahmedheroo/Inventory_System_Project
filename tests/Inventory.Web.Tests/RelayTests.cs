using System.Net;
using Inventory.Web.Api;
using Inventory.Web.Api.Contracts;
using Inventory.Web.Hubs;
using Inventory.Web.Relay;
using Inventory.Web.Security;
using Inventory.Web.Services;
using InventorySystem.Web.Tests.Fakes;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace InventorySystem.Web.Tests;

/// <summary>
/// The console's half of the live view: it holds a token of its own, follows the Inventory service's
/// hub with it, hands what it hears to the pages open here, and lets a page in only if that page has
/// a session. Each of those is a promise that fails quietly in production, so each one is pinned.
/// </summary>
public class RelayTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);

    private static ServiceApiOptions Services() =>
        ServiceApiOptions.FromConfiguration(new ConfigurationBuilder().Build());

    // ------------------------------------------------------------- relaying

    [Fact]
    public async Task A_movement_heard_from_the_inventory_service_is_handed_on_unchanged()
    {
        var recorded = new RecordingBroadcaster();
        using var relay = new StockRelay(
            Services(),
            new FixedTokenProvider(),
            recorded,
            new LiveRelayState(),
            new FakeClock(),
            NullLogger<StockRelay>.Instance);

        var movement = new LiveStockChange("RECEIVE", 7, 2, 6, 18, 10, null, null, "dock-terminal-2");

        await relay.RelayAsync(movement);

        // The same object, field for field: the page reads the service's words, not a summary of them.
        Assert.Same(movement, Assert.Single(recorded.Sent));
        Assert.Equal(6, recorded.Sent[0].Quantity);
        Assert.Equal("dock-terminal-2", recorded.Sent[0].Origin);
    }

    [Fact]
    public void The_method_a_page_listens_for_is_the_one_the_inventory_service_broadcasts()
    {
        // The Inventory service's hub broadcast name, this console's client method and the name the
        // browser subscribes to are one string, because a mismatch is a screen that never moves.
        Assert.Equal("StockChanged", LiveHub.StockChangedMethod);
        Assert.Equal("RelayState", LiveHub.RelayStateMethod);
    }

    [Fact]
    public void The_relay_follows_the_hub_the_configuration_names()
    {
        var relay = new StockRelay(
            Services(),
            new FixedTokenProvider(),
            new RecordingBroadcaster(),
            new LiveRelayState(),
            new FakeClock(),
            NullLogger<StockRelay>.Instance);

        // The Inventory service's own hub by default — a server address, not a browser's.
        Assert.Equal("http://localhost:5102/hubs/stock", relay.HubUrl.ToString());

        // And the browser is never pointed there: it follows the console's hub.
        Assert.Equal("/hubs/live", LiveHub.Path);

        var configured = ServiceApiOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:InventoryApi:HubUrl"] = "http://inventory.internal:8080/hubs/stock"
            })
            .Build());

        Assert.Equal("http://inventory.internal:8080/hubs/stock", configured.InventoryHub.ToString());
    }

    [Fact]
    public void The_state_a_page_is_told_is_the_consoles_own_link_not_the_pages()
    {
        var state = new LiveRelayState();

        Assert.False(state.Following);
        Assert.Null(state.Since);

        state.FollowingSince(Now);

        Assert.True(state.Following);
        Assert.Equal(Now, state.Since);

        state.Lost(Now.AddMinutes(3));

        Assert.False(state.Following);
        Assert.Equal(Now.AddMinutes(3), state.Since);
    }

    // --------------------------------------------------------- the token

    private static ServiceTokenProvider Provider(FakeIdentityApi identity, FakeClock clock, TimeSpan? margin = null) =>
        new(
            identity,
            new ServiceCredentialOptions("console", "the-console-secret"),
            clock,
            NullLogger<ServiceTokenProvider>.Instance,
            margin ?? TimeSpan.FromMinutes(2));

    [Fact]
    public async Task The_console_asks_for_a_token_once_and_keeps_it()
    {
        var identity = new FakeIdentityApi { ExpiresAt = Now.AddHours(8).UtcDateTime };
        var clock = new FakeClock { Now = Now };
        var provider = Provider(identity, clock);

        var first = await provider.AccessTokenAsync();
        clock.Advance(TimeSpan.FromHours(1));
        var second = await provider.AccessTokenAsync();

        Assert.Equal("the-console's-own-token", first);
        Assert.Equal(first, second);
        Assert.Equal(1, identity.ServiceTokenCalls);
    }

    [Fact]
    public async Task A_token_is_replaced_before_it_stops_working()
    {
        var identity = new FakeIdentityApi { ExpiresAt = Now.AddMinutes(10).UtcDateTime };
        var clock = new FakeClock { Now = Now };
        var provider = Provider(identity, clock, margin: TimeSpan.FromMinutes(2));

        await provider.AccessTokenAsync();

        clock.Advance(TimeSpan.FromMinutes(9));        // still valid, but inside the margin
        await provider.AccessTokenAsync();

        Assert.Equal(2, identity.ServiceTokenCalls);
    }

    [Fact]
    public async Task A_burst_of_connections_costs_one_ask()
    {
        var identity = new FakeIdentityApi { ExpiresAt = Now.AddHours(8).UtcDateTime };
        var clock = new FakeClock { Now = Now };
        var provider = Provider(identity, clock);

        var tokens = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => provider.AccessTokenAsync()));

        Assert.All(tokens, token => Assert.Equal("the-console's-own-token", token));
        Assert.Equal(1, identity.ServiceTokenCalls);
    }

    [Fact]
    public async Task A_refusal_is_not_kept_as_if_it_were_a_token()
    {
        var identity = new FakeIdentityApi
        {
            ExpiresAt = Now.AddHours(8).UtcDateTime,
            FailServiceTokenWith = new ApiException(
                HttpStatusCode.Unauthorized,
                "Identity service",
                "The Identity service answered 401 for /api/identity/token.",
                "Invalid client credentials.")
        };

        var clock = new FakeClock { Now = Now };
        var provider = Provider(identity, clock);

        await Assert.ThrowsAsync<ApiException>(() => provider.AccessTokenAsync());

        // The console's account was wrong, and then it was fixed: the next attempt has to ask again
        // rather than hand out the failure it remembered.
        identity.FailServiceTokenWith = null;
        var token = await provider.AccessTokenAsync();

        Assert.Equal("the-console's-own-token", token);
        Assert.Equal(2, identity.ServiceTokenCalls);
    }

    // -------------------------------------------------------- the console's hub

    [Fact]
    public async Task An_anonymous_browser_cannot_open_the_console_hub()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();
        await using var connection = Connect(factory, cookie: null);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync());

        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task A_signed_in_page_is_told_about_a_movement_the_relay_heard()
    {
        using var factory = ConsoleFactory.WithAStockedConsole();

        // No automatic redirects: the session cookie is set on the sign-in response itself, and a
        // client that followed it would be looking at the page it was redirected to instead.
        using var browser = factory.CreateClient(
            new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var cookie = await factory.SignInAsync(browser, "clerk", "clerk-password-2026");

        Assert.False(string.IsNullOrEmpty(cookie), "the sign-in should have set a session cookie");

        await using var connection = Connect(factory, cookie);

        var movement = new TaskCompletionSource<LiveStockChange>(TaskCreationOptions.RunContinuationsAsynchronously);
        var relayState = new TaskCompletionSource<RelayStateMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<LiveStockChange>(LiveHub.StockChangedMethod, change => movement.TrySetResult(change));
        connection.On<RelayStateMessage>(LiveHub.RelayStateMethod, state => relayState.TrySetResult(state));

        await connection.StartAsync();

        // Told on connect what the console's own link is doing: the relay is not running in this test,
        // so the honest answer is "not following".
        Assert.False((await relayState.Task.WaitAsync(TimeSpan.FromSeconds(10))).Following);

        var broadcaster = factory.Services.GetRequiredService<ILiveBroadcaster>();

        await broadcaster.StockChangedAsync(
            new LiveStockChange("ISSUE", 1, 1, 3, 1, 10, null, null, null),
            CancellationToken.None);


        var heard = await movement.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(1, heard.ProductId);
        Assert.Equal(3, heard.Quantity);
        Assert.Equal(1, heard.CurrentQuantity);
    }

    [Fact]
    public void The_consoles_token_is_the_account_the_configuration_names()
    {
        var credentials = ServiceCredentialOptions.FromConfiguration(new ConfigurationBuilder().Build());

        Assert.Equal("console", credentials.ClientId);
        Assert.Equal("console-relay-development-secret", credentials.ClientSecret);

        var configured = ServiceCredentialOptions.FromConfiguration(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Services:IdentityApi:ClientId"] = "console-inventory",
                ["Services:IdentityApi:ClientSecret"] = "a-real-secret-nobody-read"
            })
            .Build());

        Assert.Equal("console-inventory", configured.ClientId);
        Assert.Equal("a-real-secret-nobody-read", configured.ClientSecret);
    }

    [Fact]
    public void The_role_names_the_console_draws_are_the_ones_identity_issues()
    {
        // The console references no service, so what it knows about roles is pinned as literals here
        // and in Identity's own test.
        Assert.Equal("viewer", ConsoleRoles.Viewer);
        Assert.Equal("clerk", ConsoleRoles.Clerk);
        Assert.Equal("manager", ConsoleRoles.Manager);

        Assert.Contains(ConsoleRoles.Clerk, ConsoleRoles.Operators);
        Assert.Contains(ConsoleRoles.Manager, ConsoleRoles.Operators);
        Assert.DoesNotContain(ConsoleRoles.Viewer, ConsoleRoles.Operators);
    }

    private static HubConnection Connect(ConsoleFactory factory, string? cookie)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, LiveHub.Path.TrimStart('/')),
                options =>
                {
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;

                    if (cookie is not null)
                    {
                        options.Headers["Cookie"] = cookie;
                    }
                });

        return builder.Build();
    }

    private sealed class RecordingBroadcaster : ILiveBroadcaster
    {
        public List<LiveStockChange> Sent { get; } = [];

        public Task StockChangedAsync(LiveStockChange change, CancellationToken cancellationToken)
        {
            Sent.Add(change);

            return Task.CompletedTask;
        }
    }

    private sealed class FixedTokenProvider : IServiceTokenProvider
    {
        public Task<string> AccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult("a-token-the-relay-would-present");
    }
}
