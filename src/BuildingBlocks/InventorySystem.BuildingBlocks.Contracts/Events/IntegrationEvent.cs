namespace InventorySystem.BuildingBlocks.Contracts.Events;

/// <summary>
/// Base type for every event published on the message bus.
/// The <see cref="EventId"/> is used by consumers for idempotency (duplicate detection).
/// </summary>
public abstract record IntegrationEvent
{
    public Guid EventId { get; init; } = Guid.NewGuid();

    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Stable routing key / event name used when publishing (see <see cref="Messaging.MessagingTopology"/>).
    /// </summary>
    public abstract string EventName { get; }
}
