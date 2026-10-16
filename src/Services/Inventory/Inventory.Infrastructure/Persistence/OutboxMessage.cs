using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Contracts.Messaging;

namespace InventorySystem.Stock.Infrastructure.Persistence;

/// <summary>
/// A stock event waiting to be handed to the message broker.
///
/// The row is written in the same transaction as the stock change, so it is the
/// authoritative record that the event has to happen. It is only marked as
/// processed once the broker has confirmed receipt; until then the dispatcher
/// keeps retrying it, even across a restart of the service.
/// </summary>
public class OutboxMessage
{
    public const int MaxErrorLength = 1000;

    // Required by EF Core.
    private OutboxMessage()
    {
        EventName = string.Empty;
        Payload = string.Empty;
    }

    private OutboxMessage(Guid id, string eventName, string payload, DateTime occurredAt)
    {
        Id = id;
        EventName = eventName;
        Payload = payload;
        OccurredAt = occurredAt;
        NextAttemptAt = occurredAt;
    }

    /// <summary>The event id, so a redelivered message stays de-duplicable.</summary>
    public Guid Id { get; private set; }

    /// <summary>Routing key the event is published with.</summary>
    public string EventName { get; private set; }

    /// <summary>The serialized integration event (see <see cref="EventSerializer"/>).</summary>
    public string Payload { get; private set; }

    public DateTime OccurredAt { get; private set; }

    /// <summary>Set once the broker confirmed the event.</summary>
    public DateTime? ProcessedAt { get; private set; }

    public int Attempts { get; private set; }

    public string? LastError { get; private set; }

    /// <summary>Earliest time the dispatcher may try this message again (backoff).</summary>
    public DateTime NextAttemptAt { get; private set; }

    public bool IsPending => ProcessedAt is null;

    public static OutboxMessage For(IntegrationEvent integrationEvent) => new(
        integrationEvent.EventId,
        integrationEvent.EventName,
        EventSerializer.Serialize(integrationEvent),
        integrationEvent.Timestamp);

    /// <summary>Rebuilds the event so the dispatcher can publish it through the transport.</summary>
    public IntegrationEvent ToIntegrationEvent() => EventSerializer.Deserialize(EventName, Payload);

    public void MarkProcessed(DateTime processedAt)
    {
        ProcessedAt = processedAt;
        LastError = null;
    }

    /// <summary>Records a failed attempt and pushes the next try back (exponential backoff).</summary>
    public void MarkFailed(string error, DateTime nextAttemptAt)
    {
        Attempts++;
        LastError = error.Length > MaxErrorLength ? error[..MaxErrorLength] : error;
        NextAttemptAt = nextAttemptAt;
    }
}
