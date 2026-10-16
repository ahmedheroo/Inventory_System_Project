namespace InventorySystem.Alerts.Application.Abstractions;

/// <summary>
/// Remembers which inventory events this service has already applied.
///
/// Delivery from the broker is at-least-once (an event that is redelivered after
/// a crash, or re-published by the Inventory service's outbox after a failed
/// confirm, arrives twice), so the consumer has to be idempotent: the record
/// written here — in the same transaction as the alerts it caused — is what
/// turns a duplicate delivery into a no-op.
/// </summary>
public interface IProcessedEventStore
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);

    void Add(Guid eventId, string eventName, DateTime processedAt);
}
