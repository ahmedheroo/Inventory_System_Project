using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Stock.Api.Hubs;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Application.Notifications;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// The live stock view is the one thing in this service a browser could once open on its own. These
/// are the tests that say it cannot any more: a connection needs a token with the stock-read scope,
/// every other token is turned away, and the relay that does hold a token still receives everything a
/// screen needs.
/// </summary>
public class StockHubAuthorisationTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static InventoryApiFactory CreateFactory(bool broadcastThroughSignalR = false)
    {
        var factory = new InventoryApiFactory { BroadcastThroughSignalR = broadcastThroughSignalR };

        factory.Catalog
            .WithProduct(1, minimumStockLevel: 10)
            .WithWarehouse(1);

        return factory;
    }

    private static Task<HttpResponseMessage> Negotiate(InventoryApiFactory factory, string? token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/hubs/stock/negotiate?negotiateVersion=1");

        if (token is not null)
        {
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {token}");
        }

        return factory.CreateClient().SendAsync(request, Ct);
    }

    [Fact]
    public void The_service_validates_with_the_tokens_its_configuration_names()
    {
        using var factory = CreateFactory();

        // Guards the wiring the other tests rely on: if this ever disagreed with the factory's own
        // tokens, every refusal below would pass for the wrong reason.
        Assert.Equal(InventoryApiFactory.Tokens, factory.Services.GetRequiredService<JwtOptions>());
    }

    private static HubConnection Connect(InventoryApiFactory factory, string? token)
    {
        var builder = new HubConnectionBuilder()
            .WithUrl(
                new Uri(factory.Server.BaseAddress, "hubs/stock"),
                options =>
                {
                    // The whole connection is routed into the running application instead of a socket.
                    options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
                    options.Transports = HttpTransportType.LongPolling;

                    if (token is not null)
                    {
                        options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                    }
                });

        return builder.Build();
    }

    [Fact]
    public async Task An_anonymous_client_cannot_even_negotiate_a_connection()
    {
        using var factory = CreateFactory();

        using var response = await Negotiate(factory, token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_token_without_the_stock_scope_is_refused()
    {
        using var factory = CreateFactory();

        // A real token, correctly signed, that was never granted the right to watch stock: the scope
        // is the whole of the decision, and a valid token is not enough.
        using var response = await Negotiate(factory, InventoryApiFactory.TokenFor("catalogue.read"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_console_relay_token_may_connect()
    {
        using var factory = CreateFactory();

        using var response = await Negotiate(factory, InventoryApiFactory.TokenFor(JwtScopes.StockRead));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task The_token_may_travel_in_the_query_string_where_a_websocket_cannot_carry_a_header()
    {
        using var factory = CreateFactory();

        var token = InventoryApiFactory.TokenFor(JwtScopes.StockRead);

        using var response = await factory.CreateClient().PostAsync(
            $"/hubs/stock/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}",
            content: null,
            Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_connection_started_without_a_token_never_opens()
    {
        using var factory = CreateFactory();
        await using var connection = Connect(factory, token: null);

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync(Ct));

        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task A_token_without_the_scope_never_opens_a_connection_either()
    {
        using var factory = CreateFactory();
        await using var connection = Connect(factory, InventoryApiFactory.TokenFor("stock.write"));

        await Assert.ThrowsAnyAsync<Exception>(() => connection.StartAsync(Ct));

        // Writing stock is not watching it: the operation scope does not open the live view.
        Assert.Equal(HubConnectionState.Disconnected, connection.State);
    }

    [Fact]
    public async Task The_relay_that_does_hold_a_token_still_receives_every_committed_movement()
    {
        using var factory = CreateFactory(broadcastThroughSignalR: true);
        await using var connection = Connect(factory, InventoryApiFactory.TokenFor(JwtScopes.StockRead));

        var received = new TaskCompletionSource<StockChangeNotification>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        connection.On<StockChangeNotification>(nameof(IStockClient.StockChanged), notice => received.TrySetResult(notice));

        await connection.StartAsync(Ct);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/receive")
        {
            Content = JsonContent.Create(new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 6 })
        };

        request.Headers.TryAddWithoutValidation("X-Origin-Id", "dock-terminal-2");

        using var response = await factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var notice = await received.Task.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(StockChangeKind.RECEIVE, notice.Change);
        Assert.Equal(1, notice.ProductId);
        Assert.Equal(1, notice.WarehouseId);
        Assert.Equal(6, notice.Quantity);
        Assert.Equal(6, notice.CurrentQuantity);

        // The label the caller named still travels with the movement, token or no token.
        Assert.Equal("dock-terminal-2", notice.Origin);
    }

    [Fact]
    public void The_door_is_the_scope_the_console_asks_identity_for()
    {
        // The console cannot reference this service, so the two things that have to agree are pinned
        // here and in the console's own test: the policy on the hub, and the scope it requires.
        var authorize = Assert.Single(
            typeof(StockHub).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Cast<AuthorizeAttribute>());

        Assert.Equal(JwtPolicies.StockRead, authorize.Policy);
        Assert.Equal("stock:read", authorize.Policy);
        Assert.Equal("stock.read", JwtScopes.StockRead);
        Assert.Null(authorize.Roles);
    }
}
