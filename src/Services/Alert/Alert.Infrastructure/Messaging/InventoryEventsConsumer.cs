using InventorySystem.Alerts.Application.Services;
using InventorySystem.BuildingBlocks.Contracts.Messaging;
using InventorySystem.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Alerts.Infrastructure.Messaging;

/// <summary>
/// The Alert service's consumer (spec section 7.3, TASK-048): every stock event
/// from the inventory exchange is handed to <see cref="InventoryEventProcessor"/>
/// inside its own DI scope, so each message gets a fresh DbContext.
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
                "RabbitMq:Enabled is false — the Alert service will not consume inventory events.");

            return;
        }

        await _consumer.ConsumeAsync(
            MessagingTopology.AlertQueue,
            MessagingTopology.AllStockEventsRoutingKey,
            HandleAsync,
            stoppingToken);
    }

    private async Task HandleAsync(
        BuildingBlocks.Contracts.Events.IntegrationEvent integrationEvent,
        CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var processor = scope.ServiceProvider.GetRequiredService<InventoryEventProcessor>();

        var result = await processor.ProcessAsync(integrationEvent, cancellationToken);

        if (result.Duplicate)
        {
            _logger.LogDebug("{EventName} (event {EventId}) was already applied.", integrationEvent.EventName, result.EventId);

            return;
        }

        _logger.LogInformation(
            "Processed {EventName} (event {EventId}): {Raised} raised, {Refreshed} refreshed, {Resolved} resolved.",
            integrationEvent.EventName,
            result.EventId,
            result.Raised,
            result.Refreshed,
            result.Resolved);
    }
}
