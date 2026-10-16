using InventorySystem.Alerts.Application.Abstractions;
using InventorySystem.Alerts.Domain.Entities;
using InventorySystem.Alerts.Domain.Rules;
using InventorySystem.BuildingBlocks.Contracts;
using InventorySystem.BuildingBlocks.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Alerts.Application.Services;

/// <summary>What one consumed event changed; asserted by the tests and logged.</summary>
public sealed record AlertProcessingResult(Guid EventId, int Raised, int Refreshed, int Resolved, bool Duplicate)
{
    public static AlertProcessingResult AlreadyProcessed(Guid eventId) => new(eventId, 0, 0, 0, true);

    public int Changes => Raised + Refreshed + Resolved;
}

/// <summary>
/// Turns inventory events into alerts (spec 7.3, TASK-048 → TASK-050 and
/// TASK-053).
///
/// One event may affect up to two stock levels (a transfer affects both
/// warehouses), and for each of them the same three outcomes are possible:
///
/// * quantity at/below the minimum and no active alert → raise one;
/// * quantity at/below the minimum and an active alert exists → refresh it
///   (this is the duplicate-alert rule: one alert per condition, not per event);
/// * quantity back above the minimum and an active alert exists → resolve it.
///
/// The de-duplication record is written in the same <c>SaveChanges</c> as those
/// changes, so a redelivered event can neither raise a second alert nor resolve
/// one twice.
/// </summary>
public class InventoryEventProcessor
{
    private readonly IAlertRepository _alerts;
    private readonly IProcessedEventStore _processedEvents;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ILogger<InventoryEventProcessor> _logger;

    public InventoryEventProcessor(
        IAlertRepository alerts,
        IProcessedEventStore processedEvents,
        IUnitOfWork unitOfWork,
        ILogger<InventoryEventProcessor> logger)
    {
        _alerts = alerts;
        _processedEvents = processedEvents;
        _unitOfWork = unitOfWork;
        _logger = logger;
    }

    /// <summary>The stock levels an event reports, in the order they are announced.</summary>
    public static IReadOnlyList<StockLevel> AffectedStockLevels(IntegrationEvent integrationEvent) =>
        integrationEvent switch
        {
            StockReceivedEvent received =>
            [
                new(received.ProductId, received.WarehouseId, received.CurrentQuantity, received.MinimumStockLevel)
            ],
            StockIssuedEvent issued =>
            [
                new(issued.ProductId, issued.WarehouseId, issued.CurrentQuantity, issued.MinimumStockLevel)
            ],
            StockAdjustedEvent adjusted =>
            [
                new(adjusted.ProductId, adjusted.WarehouseId, adjusted.NewQuantity, adjusted.MinimumStockLevel)
            ],
            StockTransferredEvent transferred =>
            [
                new(transferred.ProductId, transferred.SourceWarehouseId, transferred.SourceCurrentQuantity, transferred.MinimumStockLevel),
                new(transferred.ProductId, transferred.DestinationWarehouseId, transferred.DestinationCurrentQuantity, transferred.MinimumStockLevel)
            ],
            _ => throw new NotSupportedException(
                $"Event '{integrationEvent.EventName}' is not handled by the Alert service.")
        };

    public async Task<AlertProcessingResult> ProcessAsync(
        IntegrationEvent integrationEvent,
        CancellationToken cancellationToken = default)
    {
        if (await _processedEvents.ExistsAsync(integrationEvent.EventId, cancellationToken))
        {
            _logger.LogInformation(
                "Event {EventName} (event {EventId}) was already processed; ignoring the duplicate.",
                integrationEvent.EventName,
                integrationEvent.EventId);

            return AlertProcessingResult.AlreadyProcessed(integrationEvent.EventId);
        }

        var now = DateTime.UtcNow;
        var raised = 0;
        var refreshed = 0;
        var resolved = 0;

        foreach (var level in AffectedStockLevels(integrationEvent))
        {
            var alert = await _alerts.FindActiveAsync(level.ProductId, level.WarehouseId, cancellationToken);
            var type = LowStockDetector.Detect(level.Quantity, level.MinimumStockLevel);

            if (type is null)
            {
                if (alert is not null)
                {
                    alert.Resolve(now, integrationEvent.EventId);
                    resolved++;

                    _logger.LogInformation(
                        "Alert {AlertId} (product {ProductId}, warehouse {WarehouseId}) resolved: {Quantity} is above the minimum {Minimum}.",
                        alert.Id,
                        level.ProductId,
                        level.WarehouseId,
                        level.Quantity,
                        level.MinimumStockLevel);
                }

                continue;
            }

            var message = LowStockDetector.Describe(
                level.ProductId,
                level.WarehouseId,
                type.Value,
                level.Quantity,
                level.MinimumStockLevel);

            if (alert is null)
            {
                alert = Alert.Raise(
                    level.ProductId,
                    level.WarehouseId,
                    type.Value,
                    message,
                    level.Quantity,
                    level.MinimumStockLevel,
                    integrationEvent.EventId,
                    now);

                _alerts.Add(alert);
                raised++;

                _logger.LogWarning(
                    "{Type} alert raised for product {ProductId} in warehouse {WarehouseId}: {Quantity} available, minimum {Minimum}.",
                    type.Value,
                    level.ProductId,
                    level.WarehouseId,
                    level.Quantity,
                    level.MinimumStockLevel);
            }
            else
            {
                alert.Refresh(
                    type.Value,
                    message,
                    level.Quantity,
                    level.MinimumStockLevel,
                    integrationEvent.EventId,
                    now);

                refreshed++;
            }
        }

        // Same transaction as the alert changes: a duplicate delivery of this very
        // event will be recognised and skipped.
        _processedEvents.Add(integrationEvent.EventId, integrationEvent.EventName, now);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return new AlertProcessingResult(integrationEvent.EventId, raised, refreshed, resolved, Duplicate: false);
    }
}

/// <summary>A stock level reported by an event.</summary>
public sealed record StockLevel(int ProductId, int WarehouseId, int Quantity, int MinimumStockLevel);
