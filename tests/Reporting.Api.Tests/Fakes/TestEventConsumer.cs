using System.Threading.Channels;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Messaging;

namespace InventorySystem.Reports.Api.Tests.Fakes;

/// <summary>
/// An in-memory stand-in for the RabbitMQ consumer.
///
/// It implements the same <see cref="IEventConsumer"/> contract the transport
/// does, so the real <c>InventoryEventsConsumer</c> background service, the real
/// projector and the real database are all exercised — only the broker hop is
/// replaced. Handlers run on the consuming loop, one message at a time, exactly
/// like the real consumer does.
/// </summary>
public class TestEventConsumer : IEventConsumer
{
    private readonly Channel<IntegrationEvent> _channel = Channel.CreateUnbounded<IntegrationEvent>();

    /// <summary>Queues the service asked to consume.</summary>
    public List<string> Queues { get; } = new();

    /// <summary>Routing keys the service bound to.</summary>
    public List<string> RoutingKeys { get; } = new();

    /// <summary>Errors the handler threw (what the real consumer would retry / dead-letter).</summary>
    public List<Exception> Failures { get; } = new();

    public int Handled { get; private set; }

    public Task ConsumeAsync(
        string queue,
        string routingKey,
        Func<IntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        lock (Queues)
        {
            Queues.Add(queue);
            RoutingKeys.Add(routingKey);
        }

        return ConsumeLoopAsync(handler, cancellationToken);
    }

    private async Task ConsumeLoopAsync(
        Func<IntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        await foreach (var integrationEvent in _channel.Reader.ReadAllAsync(cancellationToken))
        {
            try
            {
                await handler(integrationEvent, cancellationToken);
                Handled++;
            }
            catch (Exception exception)
            {
                lock (Failures)
                {
                    Failures.Add(exception);
                }
            }
        }
    }

    /// <summary>Delivers an event to the service under test, as the broker would.</summary>
    public void Publish(IntegrationEvent integrationEvent) => _channel.Writer.TryWrite(integrationEvent);
}
