using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Contracts.Messaging;
using InventorySystem.Stock.Infrastructure.Persistence;

namespace InventorySystem.CrossService.Tests;

/// <summary>
/// The inventory event contract, asserted across the two sides that have to agree
/// on it: the publisher (Inventory) writes the routing key and the payload, the
/// consumer (Alert) resolves both back into an event. A change that breaks one
/// side — renaming an event, adding an unregistered type, altering the JSON — is
/// caught here instead of silently dead-lettering messages in production.
/// </summary>
public class InventoryEventContractTests
{
    [Fact]
    public void Every_published_event_has_a_unique_routing_key_that_resolves_back_to_it()
    {
        var routingKeys = new List<string>();

        foreach (var eventType in EventRegistry.All)
        {
            var routingKey = EventRegistry.RoutingKeyOf(eventType);

            routingKeys.Add(routingKey);

            Assert.True(EventRegistry.TryResolve(routingKey, out var resolved));
            Assert.Equal(eventType, resolved);
        }

        Assert.Equal(routingKeys.Count, routingKeys.Distinct().Count());
    }

    [Fact]
    public void The_routing_keys_are_the_names_from_the_specification()
    {
        Assert.Equal("stock.received", EventRegistry.RoutingKeyOf<StockReceivedEvent>());
        Assert.Equal("stock.issued", EventRegistry.RoutingKeyOf<StockIssuedEvent>());
        Assert.Equal("stock.transferred", EventRegistry.RoutingKeyOf<StockTransferredEvent>());
        Assert.Equal("stock.adjusted", EventRegistry.RoutingKeyOf<StockAdjustedEvent>());
    }

    [Fact]
    public void An_unregistered_routing_key_is_rejected_explicitly()
    {
        Assert.False(EventRegistry.TryResolve("stock.deleted", out _));

        var exception = Assert.Throws<NotSupportedException>(() => EventRegistry.Resolve("stock.deleted"));

        Assert.Contains("stock.deleted", exception.Message);
    }

    [Fact]
    public void An_event_survives_the_publisher_consumer_round_trip()
    {
        var original = new StockTransferredEvent
        {
            ProductId = 4,
            SourceWarehouseId = 1,
            DestinationWarehouseId = 2,
            Quantity = 9,
            SourceCurrentQuantity = 3,
            DestinationCurrentQuantity = 9,
            MinimumStockLevel = 5
        };

        var consumerView = EventSerializer.Deserialize(
            EventRegistry.RoutingKeyOf<StockTransferredEvent>(),
            EventSerializer.Serialize(original));

        var received = Assert.IsType<StockTransferredEvent>(consumerView);

        Assert.Equal(original.EventId, received.EventId);            // idempotency key survives
        Assert.Equal(original.Timestamp, received.Timestamp);
        Assert.Equal(original.EventName, received.EventName);
        Assert.Equal(original.ProductId, received.ProductId);
        Assert.Equal(original.SourceWarehouseId, received.SourceWarehouseId);
        Assert.Equal(original.DestinationWarehouseId, received.DestinationWarehouseId);
        Assert.Equal(original.Quantity, received.Quantity);
        Assert.Equal(original.SourceCurrentQuantity, received.SourceCurrentQuantity);
        Assert.Equal(original.DestinationCurrentQuantity, received.DestinationCurrentQuantity);
        Assert.Equal(original.MinimumStockLevel, received.MinimumStockLevel);
    }

    [Theory]
    [InlineData(typeof(StockReceivedEvent))]
    [InlineData(typeof(StockIssuedEvent))]
    [InlineData(typeof(StockAdjustedEvent))]
    public void The_camel_case_payload_shape_is_stable(Type eventType)
    {
        var integrationEvent = (IntegrationEvent)Activator.CreateInstance(eventType)!;
        var payload = EventSerializer.Serialize(integrationEvent);

        Assert.Contains("\"eventId\"", payload);
        Assert.Contains("\"timestamp\"", payload);
        Assert.DoesNotContain("\"EventId\"", payload);

        var deserialized = EventSerializer.Deserialize(EventRegistry.RoutingKeyOf(eventType), payload);

        Assert.Equal(eventType, deserialized.GetType());
    }

    [Fact]
    public void The_outbox_payload_the_inventory_service_stores_is_readable_by_a_consumer()
    {
        // The strongest form of the contract: the exact bytes the outbox commits
        // (and the dispatcher will publish) deserialize into the same event.
        var original = new StockIssuedEvent
        {
            ProductId = 2,
            WarehouseId = 3,
            Quantity = 5,
            CurrentQuantity = 5,
            MinimumStockLevel = 7
        };

        var outboxMessage = OutboxMessage.For(original);
        var consumerView = Assert.IsType<StockIssuedEvent>(outboxMessage.ToIntegrationEvent());

        Assert.Equal(original.EventId, consumerView.EventId);
        Assert.Equal(original.EventName, outboxMessage.EventName);
        Assert.Equal(original.CurrentQuantity, consumerView.CurrentQuantity);
        Assert.Equal(original.MinimumStockLevel, consumerView.MinimumStockLevel);

        // ...and it is the same event a consumer deriving its own payload would get.
        Assert.Equal(
            consumerView,
            EventSerializer.Deserialize(outboxMessage.EventName, outboxMessage.Payload));
    }

    [Fact]
    public void The_queue_topology_matches_the_documented_names()
    {
        Assert.Equal("inventory.events", MessagingTopology.InventoryExchange);
        Assert.Equal("inventory.events.dlx", MessagingTopology.DeadLetterExchange);
        Assert.Equal("alert.inventory", MessagingTopology.AlertQueue);
        Assert.Equal("reporting.inventory", MessagingTopology.ReportingQueue);
        Assert.Equal("stock.*", MessagingTopology.AllStockEventsRoutingKey);
        Assert.Equal("alert.inventory.dead", MessagingTopology.DeadLetterQueue(MessagingTopology.AlertQueue));
    }

    [Fact]
    public void Every_registered_event_is_caught_by_the_consumer_binding()
    {
        // The Alert service binds "stock.*": every event the Inventory service can
        // publish has to match it, otherwise an alert would never be raised.
        var pattern = MessagingTopology.AllStockEventsRoutingKey.TrimEnd('*');

        Assert.All(
            EventRegistry.All,
            eventType => Assert.StartsWith(pattern, EventRegistry.RoutingKeyOf(eventType)));
    }
}
