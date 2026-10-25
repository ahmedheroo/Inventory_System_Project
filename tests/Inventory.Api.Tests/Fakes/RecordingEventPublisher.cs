using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Messaging;

namespace InventorySystem.Stock.Api.Tests.Fakes;

/// <summary>
/// Stands in for the RabbitMQ transport so the outbox dispatcher can be tested
/// without a broker: it records what was published and can be made to fail, which
/// is how the "an event is never lost" behaviour is proven.
/// </summary>
public class RecordingEventPublisher : IEventPublisher
{
    private readonly List<IntegrationEvent> _events = new();

    /// <summary>When set, every publish fails with this exception (broker down).</summary>
    public Exception? Failure { get; set; }

    public int Attempts { get; private set; }

    public IReadOnlyList<IntegrationEvent> Events
    {
        get
        {
            lock (_events)
            {
                return _events.ToList();
            }
        }
    }

    public Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        Attempts++;

        if (Failure is not null)
        {
            return Task.FromException(Failure);
        }

        lock (_events)
        {
            _events.Add(integrationEvent);
        }

        return Task.CompletedTask;
    }

    public T Single<T>() where T : IntegrationEvent
    {
        var match = Events.OfType<T>().ToList();

        Assert.Single(match);

        return match[0];
    }
}
