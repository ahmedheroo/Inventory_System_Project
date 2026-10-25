using InventorySystem.BuildingBlocks.Contracts.Messaging;
using InventorySystem.BuildingBlocks.Messaging;
using InventorySystem.Reports.Application.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Reports.Infrastructure.Messaging;

/// <summary>
/// The Reporting service's consumer (spec section 7.4, TASK-057): every stock
/// event from the inventory exchange is handed to
/// <see cref="InventoryEventProjector"/> inside its own DI scope, so each message
/// gets a fresh DbContext.
///
/// It consumes its own queue (<c>reporting.inventory</c>), so a slow report
/// projection cannot hold up the Alert service's low-stock detection.
///
/// The transport (queue declaration, acknowledgement, retry and dead-lettering)
/// belongs to <see cref="IEventConsumer"/>; this class only owns the "what to do
/// with an event" decision, which is why the tests can drive it with an in-memory
/// feed instead of a broker.
/// </summary>
public class InventoryEventsConsumer : BackgroundService
{
    private readonly IEventConsumer _consumer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<InventoryEventsConsumer> _logger;

    public InventoryEventsConsumer(
        IEventConsumer consumer,
        IServiceScopeFactory scopeFactory,
        RabbitMqOptions options,
        ILogger<InventoryEventsConsumer> logger)
    {
        _consumer = consumer;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogWarning(
                "RabbitMq:Enabled is false — the Reporting service will not consume inventory events.");

            return;
        }

        await _consumer.ConsumeAsync(
            MessagingTopology.ReportingQueue,
            MessagingTopology.AllStockEventsRoutingKey,
            HandleAsync,
            stoppingToken);
    }

    private async Task HandleAsync(
        BuildingBlocks.Contracts.Events.IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var projector = scope.ServiceProvider.GetRequiredService<InventoryEventProjector>();

        var result = await projector.ProjectAsync(integrationEvent, cancellationToken);

        if (result.Duplicate)
        {
            _logger.LogDebug(
                "{EventName} (event {EventId}) was already projected.",
                integrationEvent.EventName,
                result.EventId);

            return;
        }

        _logger.LogInformation(
            "Projected {EventName} (event {EventId}): {StockLevels} stock level(s), {Movements} movement(s).",
            integrationEvent.EventName,
            result.EventId,
            result.StockLevels,
            result.Movements);
    }
}
