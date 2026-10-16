using InventorySystem.BuildingBlocks.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Stock.Infrastructure.Messaging;

/// <summary>
/// Background service that drains the outbox to RabbitMQ.
///
/// It is the only component that touches the broker for publishing, which means
/// an HTTP request never waits for (or fails because of) the message bus: the
/// stock change is committed with its event, and delivery happens afterwards and
/// independently. A broker outage therefore degrades nothing but the event
/// latency — the events pile up in the outbox and are delivered when the broker
/// comes back.
/// </summary>
public class OutboxDispatcher : BackgroundService
{
    private readonly OutboxProcessor _processor;
    private readonly OutboxOptions _options;
    private readonly RabbitMqOptions _messagingOptions;
    private readonly ILogger<OutboxDispatcher> _logger;

    public OutboxDispatcher(
        OutboxProcessor processor,
        OutboxOptions options,
        RabbitMqOptions messagingOptions,
        ILogger<OutboxDispatcher> logger)
    {
        _processor = processor;
        _options = options;
        _messagingOptions = messagingOptions;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_messagingOptions.Enabled)
        {
            _logger.LogWarning(
                "RabbitMq:Enabled is false — the outbox dispatcher will not run and events stay queued in InventoryDb.");

            return;
        }

        _logger.LogInformation(
            "Outbox dispatcher started (poll every {PollSeconds}s, batch {BatchSize}).",
            _options.PollSeconds,
            _options.BatchSize);

        var interval = TimeSpan.FromSeconds(Math.Max(1, _options.PollSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await _processor.ProcessPendingAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Never let one bad pass kill the dispatcher: pending events must
                // keep being retried for as long as the service runs.
                _logger.LogError(exception, "An outbox dispatch pass failed; retrying after the poll interval.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox dispatcher stopped.");
    }
}
