using System.Text;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Contracts.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InventorySystem.BuildingBlocks.Messaging;

/// <summary>
/// Publishes integration events to the message bus. Implemented by
/// <see cref="RabbitMqEventPublisher"/> in production and by a recording fake in
/// tests.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// RabbitMQ implementation of the transport.
///
/// Messages are published to the durable <c>inventory.events</c> topic exchange
/// with:
///
/// * <b>persistence</b> — the broker writes them to disk, so a broker restart
///   before a consumer has read them does not lose them;
/// * <b>mandatory routing</b> and <b>publisher confirms</b> — the publish task
///   only completes once the broker has confirmed the message, and it fails if
///   the event could not be routed to any queue. The caller (the Inventory
///   service's outbox dispatcher) therefore never marks an event as published
///   unless the broker has actually taken responsibility for it.
/// </summary>
public sealed class RabbitMqEventPublisher : IEventPublisher
{
    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqEventPublisher> _logger;

    public RabbitMqEventPublisher(
        IRabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqEventPublisher> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    public async Task PublishAsync(IntegrationEvent integrationEvent, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true);

        await using var channel = await connection.CreateChannelAsync(channelOptions, cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: MessagingTopology.InventoryExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var body = Encoding.UTF8.GetBytes(EventSerializer.Serialize(integrationEvent));

        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = EventSerializer.ContentType,
            ContentEncoding = "utf-8",
            MessageId = integrationEvent.EventId.ToString(),
            Type = integrationEvent.EventName,
            AppId = "inventory-service",
            Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
        };

        // mandatory: an event that cannot be routed to a queue means the message
        // would be dropped — fail instead, and let the outbox retry it.
        await channel.BasicPublishAsync(
            exchange: MessagingTopology.InventoryExchange,
            routingKey: integrationEvent.EventName,
            mandatory: true,
            basicProperties: properties,
            body: body,
            cancellationToken: cancellationToken);

        _logger.LogDebug(
            "Confirmed {EventName} (event {EventId}) on {Exchange}.",
            integrationEvent.EventName,
            integrationEvent.EventId,
            MessagingTopology.InventoryExchange);
    }
}
