using System.Text.Json;
using System.Text.Json.Serialization;
using InventorySystem.BuildingBlocks.Contracts.Events;

namespace InventorySystem.BuildingBlocks.Contracts.Messaging;

/// <summary>
/// Single place where a routing key is mapped to its event type.
///
/// Publishers use <see cref="IntegrationEvent.EventName"/> as the routing key and
/// consumers use the same registry to turn a delivered routing key back into a
/// type, so the two sides cannot drift apart: a new event that is published but
/// not registered (or the reverse) fails the contract test in
/// <c>tests/CrossService.Contracts.Tests</c> instead of silently dropping
/// messages in production.
/// </summary>
public static class EventRegistry
{
    /// <summary>Every event that may travel over the inventory exchange.</summary>
    public static IReadOnlyList<Type> All { get; } = new[]
    {
        typeof(StockReceivedEvent),
        typeof(StockIssuedEvent),
        typeof(StockTransferredEvent),
        typeof(StockAdjustedEvent)
    };

    private static readonly Dictionary<string, Type> ByRoutingKey =
        All.ToDictionary(type => RoutingKeyOf(type), StringComparer.Ordinal);

    /// <summary>The routing key an event type is published with.</summary>
    public static string RoutingKeyOf(Type eventType)
    {
        if (!typeof(IntegrationEvent).IsAssignableFrom(eventType))
        {
            throw new ArgumentException($"{eventType.Name} is not an {nameof(IntegrationEvent)}.", nameof(eventType));
        }

        // Instantiate once so the routing key always comes from the contract itself
        // (EventName is an instance member) rather than a duplicated string.
        var instance = (IntegrationEvent)Activator.CreateInstance(eventType)!;

        return instance.EventName;
    }

    public static string RoutingKeyOf<TEvent>() where TEvent : IntegrationEvent => RoutingKeyOf(typeof(TEvent));

    public static bool TryResolve(string routingKey, out Type eventType) =>
        ByRoutingKey.TryGetValue(routingKey, out eventType!);

    /// <summary>Resolves the event type for a delivered routing key.</summary>
    public static Type Resolve(string routingKey) =>
        TryResolve(routingKey, out var eventType)
            ? eventType
            : throw new NotSupportedException($"Routing key '{routingKey}' is not a known inventory event.");
}

/// <summary>
/// Serialization used on both sides of the bus and by the Inventory service's
/// outbox, so a message written by the publisher is always readable by a
/// consumer.
/// </summary>
public static class EventSerializer
{
    public const string ContentType = "application/json";

    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        WriteIndented = false
    };

    public static string Serialize(IntegrationEvent integrationEvent) =>
        JsonSerializer.Serialize(integrationEvent, integrationEvent.GetType(), Options);

    /// <summary>
    /// Rebuilds an event from its routing key and JSON payload, restoring
    /// <c>eventId</c> and <c>timestamp</c> so consumers can de-duplicate
    /// redelivered messages.
    /// </summary>
    public static IntegrationEvent Deserialize(string routingKey, string payload)
    {
        var eventType = EventRegistry.Resolve(routingKey);

        return (IntegrationEvent)(JsonSerializer.Deserialize(payload, eventType, Options)
            ?? throw new InvalidOperationException($"Payload for '{routingKey}' deserialized to null."));
    }
}
