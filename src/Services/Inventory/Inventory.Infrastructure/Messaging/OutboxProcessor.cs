using InventorySystem.BuildingBlocks.Messaging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Stock.Infrastructure.Messaging;

/// <summary>Outcome of one dispatch pass; also what the tests assert on.</summary>
public sealed record OutboxDispatchResult(int Pending, int Dispatched, int Failed)
{
    public static readonly OutboxDispatchResult Empty = new(0, 0, 0);
}

/// <summary>
/// One pass of the outbox dispatcher: read the pending events, publish them, and
/// record the outcome. Kept separate from the polling loop so it can be driven
/// directly (and deterministically) by tests.
///
/// Delivery is <b>at-least-once</b>: an event is only marked processed after the
/// broker confirmed it, so a crash between "broker has it" and "row is marked"
/// re-publishes it. Consumers therefore de-duplicate on
/// <c>IntegrationEvent.EventId</c> (see the Alert service's processed-event
/// table).
/// </summary>
public class OutboxProcessor
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _publisher;
    private readonly OutboxOptions _options;
    private readonly ILogger<OutboxProcessor> _logger;

    public OutboxProcessor(
        IServiceScopeFactory scopeFactory,
        IEventPublisher publisher,
        OutboxOptions options,
        ILogger<OutboxProcessor> logger)
    {
        _scopeFactory = scopeFactory;
        _publisher = publisher;
        _options = options;
        _logger = logger;
    }

    public async Task<OutboxDispatchResult> ProcessPendingAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<OutboxStore>();

        var pending = await store.GetPendingAsync(DateTime.UtcNow, _options.BatchSize, cancellationToken);
        if (pending.Count == 0)
        {
            return OutboxDispatchResult.Empty;
        }

        var dispatched = 0;
        var failed = 0;
        var cancelled = false;

        foreach (var message in pending)
        {
            try
            {
                await _publisher.PublishAsync(message.ToIntegrationEvent(), cancellationToken);
                message.MarkProcessed(DateTime.UtcNow);
                dispatched++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Shutting down: keep the successes recorded, and leave this message
                // pending so it is published by the next start of the service.
                cancelled = true;
                break;
            }
            catch (Exception exception)
            {
                var nextAttemptAt = DateTime.UtcNow + _options.BackoffFor(message.Attempts + 1);
                message.MarkFailed(exception.Message, nextAttemptAt);
                failed++;

                _logger.LogError(
                    exception,
                    "Publishing {EventName} (event {EventId}) failed on attempt {Attempts}; next attempt after {NextAttemptAt:o}.",
                    message.EventName,
                    message.Id,
                    message.Attempts,
                    nextAttemptAt);
            }
        }

        await store.SaveChangesAsync(CancellationToken.None);

        var result = new OutboxDispatchResult(pending.Count, dispatched, failed);

        if (dispatched > 0 || failed > 0)
        {
            _logger.LogInformation(
                "Outbox pass: {Dispatched} published, {Failed} failed, {Pending} considered.",
                result.Dispatched,
                result.Failed,
                result.Pending);
        }

        if (cancelled)
        {
            cancellationToken.ThrowIfCancellationRequested();
        }

        return result;
    }
}
