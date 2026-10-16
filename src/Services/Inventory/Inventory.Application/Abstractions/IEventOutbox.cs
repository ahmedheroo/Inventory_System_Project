using InventorySystem.BuildingBlocks.Contracts.Events;

namespace InventorySystem.Stock.Application.Abstractions;

/// <summary>
/// Stores an event in the same database transaction as the stock change it
/// describes (spec rule 5: every successful inventory modification publishes an
/// event).
///
/// This is the transactional-outbox port: <see cref="Enqueue"/> only registers
/// the event on the current unit of work, so a stock change and its event are
/// committed together by a single <c>SaveChanges</c>. Either both exist or
/// neither does — which is what makes the "event cannot be lost" guarantee hold
/// (see docs/architecture.md decision 11). The actual transport hop is performed
/// afterwards by the outbox dispatcher.
/// </summary>
public interface IEventOutbox
{
    void Enqueue(IntegrationEvent integrationEvent);
}
