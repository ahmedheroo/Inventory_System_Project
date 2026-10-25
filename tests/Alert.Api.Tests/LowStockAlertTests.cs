using System.Net.Http.Json;
using InventorySystem.Alerts.Application.Dtos;
using Microsoft.Extensions.DependencyInjection;
using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Contracts.Messaging;

namespace InventorySystem.Alerts.Api.Tests;

/// <summary>
/// Acceptance criteria of the low-stock alert (spec section 18): the inventory
/// event is received, the current quantity is obtained, the minimum stock level is
/// checked, an alert is created when the quantity is at or below the threshold,
/// duplicates are handled and the alert can be retrieved through the API.
/// </summary>
public class LowStockAlertTests
{
    private static CancellationToken Ct => CancellationToken.None;

    /// <summary>An issue that leaves <paramref name="currentQuantity"/> on hand.</summary>
    private static StockIssuedEvent Issued(int currentQuantity, int minimumStockLevel = 10, int productId = 1, int warehouseId = 1) => new()
    {
        ProductId = productId,
        WarehouseId = warehouseId,
        Quantity = 1,
        CurrentQuantity = currentQuantity,
        MinimumStockLevel = minimumStockLevel
    };

    private static StockReceivedEvent Received(int currentQuantity, int minimumStockLevel = 10) => new()
    {
        ProductId = 1,
        WarehouseId = 1,
        Quantity = 1,
        CurrentQuantity = currentQuantity,
        MinimumStockLevel = minimumStockLevel
    };

    [Fact]
    public async Task An_issue_that_reaches_the_minimum_raises_a_low_stock_alert()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        var result = await factory.ProcessAsync(Issued(currentQuantity: 10));

        Assert.Equal(1, result.Raised);
        Assert.False(result.Duplicate);

        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(1, alert.ProductId);
        Assert.Equal(1, alert.WarehouseId);
        Assert.Equal(AlertType.LOW_STOCK, alert.Type);
        Assert.Equal(AlertStatus.ACTIVE, alert.Status);
        Assert.Equal(10, alert.CurrentQuantity);
        Assert.Equal(10, alert.MinimumStockLevel);
        Assert.False(alert.IsRead);
        Assert.Contains("low on stock", alert.Message);

        // ...and it is retrievable through the API.
        var listed = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts", Ct);
        var fetched = await client.GetFromJsonAsync<AlertDto>($"/api/alerts/{alert.Id}", Ct);

        Assert.Equal(alert.Id, Assert.Single(listed!.Items).Id);
        Assert.Equal(alert.Id, fetched!.Id);
        Assert.Equal(AlertType.LOW_STOCK, fetched.Type);
        Assert.False(fetched.IsRead);
    }

    [Fact]
    public async Task Zero_stock_raises_an_out_of_stock_alert()
    {
        using var factory = new AlertApiFactory();

        var result = await factory.ProcessAsync(Issued(currentQuantity: 0));

        Assert.Equal(1, result.Raised);

        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(AlertType.OUT_OF_STOCK, alert.Type);
        Assert.Contains("out of stock", alert.Message);
    }

    [Fact]
    public async Task Stock_above_the_minimum_raises_no_alert()
    {
        using var factory = new AlertApiFactory();

        var result = await factory.ProcessAsync(Issued(currentQuantity: 11, minimumStockLevel: 10));

        Assert.Equal(0, result.Changes);
        Assert.Empty(factory.StoredAlerts());

        // The event is still recorded as processed, so a redelivery stays a no-op.
        Assert.Equal(1, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task Repeated_low_stock_events_update_one_alert_instead_of_creating_more()
    {
        using var factory = new AlertApiFactory();

        await factory.ProcessAsync(Issued(currentQuantity: 6));
        var firstId = Assert.Single(factory.StoredAlerts()).Id;

        var second = await factory.ProcessAsync(Issued(currentQuantity: 2));
        var third = await factory.ProcessAsync(Issued(currentQuantity: 0));

        Assert.Equal(1, second.Refreshed);
        Assert.Equal(1, third.Refreshed);

        // Still one row, updated in place — the duplicate-alert rule.
        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(firstId, alert.Id);
        Assert.Equal(0, alert.CurrentQuantity);
        Assert.Equal(AlertType.OUT_OF_STOCK, alert.Type);   // the condition got worse
        Assert.Contains("out of stock", alert.Message);
        Assert.Equal(3, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task A_replenishment_resolves_the_alert_and_a_later_shortage_raises_a_new_one()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await factory.ProcessAsync(Issued(currentQuantity: 4));
        var firstId = Assert.Single(factory.StoredAlerts()).Id;

        var replenished = await factory.ProcessAsync(Received(currentQuantity: 30));

        Assert.Equal(1, replenished.Resolved);

        var resolved = factory.StoredAlerts().Single(alert => alert.Id == firstId);

        Assert.Equal(AlertStatus.RESOLVED, resolved.Status);
        Assert.NotNull(resolved.ResolvedAt);

        // The default list shows the alerts that need attention...
        var active = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts", Ct);
        var resolvedOnly = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?status=RESOLVED", Ct);

        Assert.Empty(active!.Items);
        Assert.Equal(firstId, Assert.Single(resolvedOnly!.Items).Id);

        // ...and the history is kept when the product runs low again.
        await factory.ProcessAsync(Issued(currentQuantity: 1));

        var alerts = factory.StoredAlerts();

        Assert.Equal(2, alerts.Count);
        Assert.Equal(AlertStatus.RESOLVED, alerts[0].Status);
        Assert.Equal(AlertStatus.ACTIVE, alerts[1].Status);
        Assert.NotEqual(alerts[0].Id, alerts[1].Id);
    }

    [Fact]
    public async Task A_transfer_alerts_the_warehouse_that_dropped_below_the_minimum()
    {
        using var factory = new AlertApiFactory();

        var result = await factory.ProcessAsync(new StockTransferredEvent
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 5,
            SourceCurrentQuantity = 0,
            DestinationCurrentQuantity = 25,
            MinimumStockLevel = 10
        });

        Assert.Equal(1, result.Raised);

        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(1, alert.WarehouseId);
        Assert.Equal(AlertType.OUT_OF_STOCK, alert.Type);
    }

    [Fact]
    public async Task A_transfer_can_alert_both_warehouses()
    {
        using var factory = new AlertApiFactory();

        var result = await factory.ProcessAsync(new StockTransferredEvent
        {
            ProductId = 1,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 5,
            SourceCurrentQuantity = 3,
            DestinationCurrentQuantity = 0,
            MinimumStockLevel = 10
        });

        Assert.Equal(2, result.Raised);

        var alerts = factory.StoredAlerts();

        Assert.Equal(2, alerts.Count);
        Assert.Equal(new[] { 1, 2 }, alerts.Select(alert => alert.WarehouseId));

        // The source kept 3 units (low stock), the destination has none left.
        Assert.Equal(AlertType.LOW_STOCK, alerts[0].Type);
        Assert.Equal(3, alerts[0].CurrentQuantity);
        Assert.Equal(AlertType.OUT_OF_STOCK, alerts[1].Type);
        Assert.Equal(0, alerts[1].CurrentQuantity);
    }

    [Fact]
    public async Task An_adjustment_below_the_minimum_raises_an_alert()
    {
        using var factory = new AlertApiFactory();

        var result = await factory.ProcessAsync(new StockAdjustedEvent
        {
            ProductId = 3,
            WarehouseId = 2,
            OldQuantity = 20,
            NewQuantity = 5,
            MinimumStockLevel = 15
        });

        Assert.Equal(1, result.Raised);

        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(3, alert.ProductId);
        Assert.Equal(5, alert.CurrentQuantity);
        Assert.Equal(15, alert.MinimumStockLevel);
    }

    [Fact]
    public async Task A_redelivered_event_is_applied_only_once()
    {
        using var factory = new AlertApiFactory();

        var integrationEvent = Issued(currentQuantity: 5);

        var first = await factory.ProcessAsync(integrationEvent);
        var duplicate = await factory.ProcessAsync(integrationEvent);

        Assert.False(first.Duplicate);
        Assert.True(duplicate.Duplicate);
        Assert.Equal(0, duplicate.Changes);

        Assert.Single(factory.StoredAlerts());
        Assert.Equal(1, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task A_redelivered_replenishment_does_not_reopen_or_duplicate_anything()
    {
        using var factory = new AlertApiFactory();

        await factory.ProcessAsync(Issued(currentQuantity: 5));

        var replenishment = Received(currentQuantity: 50);

        await factory.ProcessAsync(replenishment);
        var duplicate = await factory.ProcessAsync(replenishment);

        Assert.True(duplicate.Duplicate);
        Assert.Single(factory.StoredAlerts());
        Assert.Equal(AlertStatus.RESOLVED, factory.StoredAlerts()[0].Status);
    }

    [Fact]
    public async Task A_later_event_does_not_mark_an_already_read_alert_unread()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        await factory.ProcessAsync(Issued(currentQuantity: 6));
        var alertId = Assert.Single(factory.StoredAlerts()).Id;

        var read = await client.PutAsync($"/api/alerts/{alertId}/read", null, Ct);
        var readAt = (await read.Content.ReadFromJsonAsync<AlertDto>(Ct))!.ReadAt;

        await factory.ProcessAsync(Issued(currentQuantity: 4));

        var alert = factory.StoredAlerts().Single(candidate => candidate.Id == alertId);

        Assert.True(alert.IsRead);
        Assert.Equal(readAt, alert.ReadAt);
        Assert.Equal(4, alert.CurrentQuantity);   // but it was still refreshed
    }

    [Fact]
    public async Task A_concurrent_redelivery_of_the_same_event_is_rejected_by_the_database()
    {
        using var factory = new AlertApiFactory();

        var integrationEvent = Issued(currentQuantity: 5);

        await factory.ProcessAsync(integrationEvent);

        // What a second consumer instance would try: insert the same event id again.
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventorySystem.Alerts.Infrastructure.Persistence.AlertDbContext>();

        dbContext.ProcessedEvents.Add(new InventorySystem.Alerts.Infrastructure.Persistence.ProcessedEvent(
            integrationEvent.EventId, integrationEvent.EventName, DateTime.UtcNow));

        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => dbContext.SaveChangesAsync(Ct));

        Assert.Equal(1, factory.ProcessedEventCount());
    }

    [Fact]
    public async Task An_event_the_service_does_not_handle_is_rejected()
    {
        using var factory = new AlertApiFactory();

        var exception = await Assert.ThrowsAsync<NotSupportedException>(
            () => factory.ProcessAsync(new UnknownStockEvent()));

        Assert.Contains("stock.unknown", exception.Message);
        Assert.Empty(factory.StoredAlerts());
    }

    [Fact]
    public async Task The_running_consumer_binds_the_alert_queue_and_projects_a_broker_event()
    {
        using var factory = new AlertApiFactory();
        var client = factory.CreateClient();

        // Start the host/consumer, then deliver an event the way RabbitMQ would.
        factory.Feed.Publish(Issued(currentQuantity: 2, productId: 9, warehouseId: 4));

        await Wait.UntilAsync(
            () => factory.StoredAlerts().Count == 1,
            "the consumer to project the delivered event into an alert");

        Assert.Equal(1, factory.Feed.Handled);
        Assert.Equal(MessagingTopology.AlertQueue, Assert.Single(factory.Feed.Queues));
        Assert.Equal(MessagingTopology.AllStockEventsRoutingKey, Assert.Single(factory.Feed.RoutingKeys));

        var alert = Assert.Single(factory.StoredAlerts());

        Assert.Equal(9, alert.ProductId);
        Assert.Equal(4, alert.WarehouseId);

        var listed = await client.GetFromJsonAsync<PagedResult<AlertDto>>("/api/alerts?productId=9", Ct);

        Assert.Single(listed!.Items);
    }

    [Fact]
    public async Task A_handler_failure_is_reported_to_the_transport_so_the_message_can_be_retried()
    {
        using var factory = new AlertApiFactory();
        _ = factory.CreateClient();

        factory.Feed.Publish(new UnknownStockEvent());

        await Wait.UntilAsync(
            () => factory.Feed.Failures.Count == 1,
            "the consumer to surface the handler failure");

        // The real consumer retries such a delivery and then dead-letters it, so the
        // message is neither lost nor able to stop the loop.
        Assert.IsType<NotSupportedException>(factory.Feed.Failures[0]);
        Assert.Empty(factory.StoredAlerts());
    }
}

/// <summary>An event the Alert service has no rule for (as a future publisher might send).</summary>
public sealed record UnknownStockEvent : IntegrationEvent
{
    public const string Name = "stock.unknown";

    public override string EventName => Name;
}
