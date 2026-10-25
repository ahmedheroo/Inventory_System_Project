namespace InventorySystem.Reports.Infrastructure.Persistence;

/// <summary>
/// One row per inventory event this service has projected. The primary key is the
/// event id, so the broker's at-least-once delivery cannot move the ledger twice
/// or overwrite a newer quantity with a stale one.
/// </summary>
public class ProcessedEvent
{
    private ProcessedEvent()
    {
        EventName = string.Empty;
    }

    public ProcessedEvent(Guid eventId, string eventName, DateTime processedAt)
    {
        EventId = eventId;
        EventName = eventName;
        ProcessedAt = processedAt;
    }

    /// <summary>The integration event's <c>EventId</c>.</summary>
    public Guid EventId { get; private set; }

    public string EventName { get; private set; }

    public DateTime ProcessedAt { get; private set; }
}
