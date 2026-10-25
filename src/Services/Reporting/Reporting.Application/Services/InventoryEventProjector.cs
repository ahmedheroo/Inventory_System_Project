using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Application.ReadModel;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Reports.Application.Services;

/// <summary>What one consumed event changed; asserted by the tests and logged.</summary>
public sealed record ProjectionResult(Guid EventId, int StockLevels, int Movements, bool Duplicate)
{
    public static ProjectionResult AlreadyProcessed(Guid eventId) => new(eventId, 0, 0, true);
}

/// <summary>
/// Projects a consumed inventory event into ReportingDb (TASK-057): it updates the
/// stock level of every (product, warehouse) pair the event reports and appends the
/// matching movement ledger entries.
///
/// The de-duplication record is written in the same <c>SaveChanges</c> as those
/// rows, so a redelivered event can neither move the ledger twice nor overwrite a
/// newer quantity with a stale one.
/// </summary>
public class InventoryEventProjector
{
    private readonly IReportingRepository _repository;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryEventProjector> _logger;

    public InventoryEventProjector(
        IReportingRepository repository,
        IProcessedEventStore processedEvents,
        IUnitOfWork unitOfWork,
        ILogger<InventoryEventProjector> logger)
    {
        _repository = repository;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    public async Task<ProjectionResult> ProjectAsync(
        IntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await _processedEvents.ExistsAsync(integrationEvent.EventId, cancellationToken))
        {
            _logger.LogInformation(
                "Event {EventName} (event {EventId}) was already projected; ignoring the duplicate.",
                integrationEvent.EventName,
                integrationEvent.EventId);

            return ProjectionResult.AlreadyProcessed(integrationEvent.EventId);
        }

        // The operation's own timestamp, not the projection's, so the movement
        // report and the date filter describe when the stock actually moved.
        var occurredAt = integrationEvent.Timestamp == default ? DateTime.UtcNow : integrationEvent.Timestamp;
        var observations = StockEventProjection.Observe(integrationEvent);

        var stockLevels = 0;
        var movements = 0;

        foreach (var observation in observations)
        {
            var stockLevel = await _repository.FindStockLevelAsync(
                observation.ProductId,
                observation.WarehouseId,
                cancellationToken);

            if (stockLevel is null)
            {
                _repository.AddStockLevel(StockLevelSnapshot.Create(
                    observation.ProductId,
                    observation.WarehouseId,
                    observation.QuantityAfter,
                    observation.MinimumStockLevel,
                    integrationEvent.EventId,
                    occurredAt));
            }
            else
            {
                stockLevel.Apply(
                    observation.QuantityAfter,
                    observation.MinimumStockLevel,
                    integrationEvent.EventId,
                    occurredAt);
            }

            stockLevels++;

            _repository.AddMovement(StockMovement.Record(
                observation.ProductId,
                observation.WarehouseId,
                observation.Type,
                observation.Quantity,
                observation.QuantityAfter,
                integrationEvent.EventId,
                occurredAt));

            movements++;
        }

        // Same transaction as the rows above: a duplicate delivery of this very
        // event will be recognised and skipped.
        _processedEvents.Add(integrationEvent.EventId, integrationEvent.EventName, DateTime.UtcNow);

        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new ProjectionResult(integrationEvent.EventId, stockLevels, movements, Duplicate: false);
    }
}
