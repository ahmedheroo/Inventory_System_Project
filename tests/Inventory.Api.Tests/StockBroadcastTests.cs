using System.Net;
using System.Net.Http.Json;
using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Stock.Api.Hubs;
using InventorySystem.Stock.Api.Services;
using InventorySystem.Stock.Application.Dtos;
using InventorySystem.Stock.Application.Notifications;

namespace InventorySystem.Stock.Api.Tests;

/// <summary>
/// Sprint 7's live view: the hub, and the notification each committed operation sends to
/// it. The assertions are the ones a screen depends on — which operation, how much moved,
/// and what the level reads afterwards — plus the two properties that keep the live view
/// honest: a refused operation broadcasts nothing, and a transfer is one notification
/// carrying both legs rather than two.
/// </summary>
public class StockBroadcastTests
{
    private static CancellationToken Ct => CancellationToken.None;

    private static InventoryApiFactory CreateFactory()
    {
        var factory = new InventoryApiFactory();

        factory.Catalog
            .WithProduct(1, minimumStockLevel: 10)
            .WithProduct(2, minimumStockLevel: 3)
            .WithWarehouse(1)
            .WithWarehouse(2);

        return factory;
    }

    /// <summary>Receives four units of product 1, optionally identifying the terminal that asked.</summary>
    private static async Task Receive(InventoryApiFactory factory, string? origin = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/receive")
        {
            Content = JsonContent.Create(new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 4 })
        };

        if (origin is not null)
        {
            request.Headers.TryAddWithoutValidation(StockChangeOrigin.HeaderName, origin);
        }

        using var response = await factory.CreateClient().SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Receiving_stock_tells_live_clients_what_was_committed()
    {
        using var factory = CreateFactory();

        await factory.CreateClient().PostAsJsonAsync(
            "/api/inventory/receive",
            new ReceiveStockRequest { ProductId = 1, WarehouseId = 2, Quantity = 20 },
            Ct);

        var notification = Assert.Single(factory.Broadcaster.Notifications);

        Assert.Equal(StockChangeKind.RECEIVE, notification.Change);
        Assert.Equal(1, notification.ProductId);
        Assert.Equal(2, notification.WarehouseId);
        Assert.Equal(20, notification.Quantity);
        Assert.Equal(20, notification.CurrentQuantity);
        Assert.Equal(10, notification.MinimumStockLevel);   // the minimum the state is read against
        Assert.Null(notification.DestinationWarehouseId);
    }

    [Fact]
    public async Task Issuing_stock_reports_the_level_it_left_behind()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/inventory/receive", new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);
        await client.PostAsJsonAsync(
            "/api/inventory/issue", new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 12 }, Ct);

        var notification = factory.Broadcaster.Notifications.Last();

        Assert.Equal(StockChangeKind.ISSUE, notification.Change);
        Assert.Equal(12, notification.Quantity);
        Assert.Equal(8, notification.CurrentQuantity);      // 8 against a minimum of 10: below it
        Assert.Equal(10, notification.MinimumStockLevel);
    }

    [Fact]
    public async Task A_transfer_is_one_notification_carrying_both_legs()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/inventory/receive", new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);
        factory.Broadcaster.Notifications.Clear();

        await client.PostAsJsonAsync(
            "/api/inventory/transfer",
            new TransferStockRequest { ProductId = 1, SourceWarehouseId = 1, DestinationWarehouseId = 2, Quantity = 8 },
            Ct);

        var notification = Assert.Single(factory.Broadcaster.Notifications);

        Assert.Equal(StockChangeKind.TRANSFER, notification.Change);
        Assert.Equal(1, notification.WarehouseId);                       // the source
        Assert.Equal(12, notification.CurrentQuantity);
        Assert.Equal(2, notification.DestinationWarehouseId);
        Assert.Equal(8, notification.DestinationCurrentQuantity);
    }

    [Fact]
    public async Task A_count_reports_the_size_of_the_correction()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/inventory/receive", new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 20 }, Ct);

        await client.PostAsJsonAsync(
            "/api/inventory/adjust", new AdjustStockRequest { ProductId = 1, WarehouseId = 1, NewQuantity = 18 }, Ct);

        var notification = factory.Broadcaster.Notifications.Last();

        Assert.Equal(StockChangeKind.ADJUSTMENT, notification.Change);
        Assert.Equal(2, notification.Quantity);           // the magnitude of the -2 correction
        Assert.Equal(18, notification.CurrentQuantity);
    }

    [Fact]
    public async Task An_operation_the_service_refused_is_never_broadcast()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        await client.PostAsJsonAsync(
            "/api/inventory/receive", new ReceiveStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 5 }, Ct);
        factory.Broadcaster.Notifications.Clear();

        var overIssue = await client.PostAsJsonAsync(
            "/api/inventory/issue", new IssueStockRequest { ProductId = 1, WarehouseId = 1, Quantity = 6 }, Ct);
        var unknownWarehouse = await client.PostAsJsonAsync(
            "/api/inventory/receive", new ReceiveStockRequest { ProductId = 1, WarehouseId = 99, Quantity = 1 }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, overIssue.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, unknownWarehouse.StatusCode);
        Assert.Empty(factory.Broadcaster.Notifications);
    }

    [Fact]
    public async Task An_operation_says_which_terminal_asked_for_it()
    {
        using var factory = CreateFactory();

        await Receive(factory, origin: "4f2ab19c");

        // The console sends the station its browser belongs to, and the notification carries it
        // back so the screen can tell its own movements from everybody else's.
        Assert.Equal("4f2ab19c", Assert.Single(factory.Broadcaster.Notifications).Origin);
    }

    [Fact]
    public async Task An_operation_that_did_not_ident_itself_is_broadcast_without_an_origin()
    {
        using var factory = CreateFactory();

        await Receive(factory);

        Assert.Null(Assert.Single(factory.Broadcaster.Notifications).Origin);
    }

    [Theory]
    [InlineData("dock-3", "dock-3")]
    [InlineData("  DOCK 7  ", "DOCK7")]
    [InlineData("!!! ***", null)]            // nothing that could be an identifier
    [InlineData("", null)]
    public async Task The_origin_is_the_label_the_service_could_safely_echo(string header, string? expected)
    {
        using var factory = CreateFactory();

        await Receive(factory, origin: header);

        Assert.Equal(expected, Assert.Single(factory.Broadcaster.Notifications).Origin);
    }

    [Fact]
    public async Task A_very_long_origin_is_cut_short_rather_than_passed_on()
    {
        using var factory = CreateFactory();

        await Receive(factory, origin: new string('a', 60));

        Assert.Equal(new string('a', 40), Assert.Single(factory.Broadcaster.Notifications).Origin);
    }

    [Fact]
    public void The_attribution_header_is_the_one_the_console_sends()
    {
        // The console references no service project, so the one wire contract the two sides share is
        // pinned here and in the console's own test.
        Assert.Equal("X-Origin-Id", StockChangeOrigin.HeaderName);
    }

    [Fact]
    public async Task The_hub_endpoint_negotiates_a_connection()
    {
        using var factory = CreateFactory();
        var client = factory.CreateClient();

        // The hub is mapped where the console's relay looks for it, and it answers a caller that
        // holds the token it asks for. That it answers nobody else is the authorisation tests' job.
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{StockHub.Path}/negotiate?negotiateVersion=1");
        request.Headers.TryAddWithoutValidation(
            "Authorization",
            $"Bearer {InventoryApiFactory.TokenFor(JwtScopes.StockRead)}");

        var response = await client.SendAsync(request, Ct);
        var body = await response.Content.ReadAsStringAsync(Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("connectionId", body);
        Assert.Contains("availableTransports", body);
    }
}
