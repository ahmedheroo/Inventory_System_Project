namespace InventorySystem.Reports.Application.Abstractions;


/// One row per inventory event this service has projected. Delivery is
/// at-least-once (see docs/architecture.md decision 14), so the event id is the
/// primary key: the broker cannot make a redelivered event count twice in the
/// reports.
/// 
public interface IProcessedEventStore
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken = default);

    void Add(Guid eventId, string eventName, DateTime processedAt);
}
