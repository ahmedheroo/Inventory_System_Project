using System.Text;
using InventorySystem.BuildingBlocks.Contracts.Events;
using InventorySystem.BuildingBlocks.Contracts.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace InventorySystem.BuildingBlocks.Messaging;

/// <summary>
/// Consumes events from a queue. Implemented by
/// <see cref="RabbitMqEventConsumer"/> in production and by an in-memory feed in
/// tests, so the consuming side of a service can be tested without a broker.
/// </summary>
public interface IEventConsumer
{
    /// <summary>
    /// Runs until <paramref name="cancellationToken"/> is cancelled, handing every
    /// delivered event to <paramref name="handler"/>.
    /// </summary>
    Task ConsumeAsync(
        string queue,
        string routingKey,
        Func<IntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// RabbitMQ consumer with the delivery guarantees the Alert and Reporting
/// services rely on:
///
/// * the queue, its dead-letter exchange and the binding are declared on start,
///   so a consumer may start before the publisher has ever run;
/// * a message is acknowledged only after the handler succeeded — a crash in
///   between leaves the message unacknowledged and the broker redelivers it;
/// * a handler failure is retried once (re-queued) and then dead-lettered, so a
///   poison message can never block the queue or disappear;
/// * an event whose routing key is not in <see cref="EventRegistry"/> is
///   dead-lettered immediately instead of crashing the consumer loop.
/// </summary>
public sealed class RabbitMqEventConsumer : IEventConsumer
{
    private static readonly TimeSpan ReconnectDelay = TimeSpan.FromSeconds(5);

    private readonly IRabbitMqConnectionProvider _connectionProvider;
    private readonly ILogger<RabbitMqEventConsumer> _logger;

    public RabbitMqEventConsumer(
        IRabbitMqConnectionProvider connectionProvider,
        ILogger<RabbitMqEventConsumer> logger)
    {
        _connectionProvider = connectionProvider;
        _logger = logger;
    }

    public async Task ConsumeAsync(
        string queue,
        string routingKey,
        Func<IntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken = default)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilShutdownAsync(queue, routingKey, handler, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    exception,
                    "Consuming '{Queue}' failed; retrying in {Seconds}s.",
                    queue,
                    ReconnectDelay.TotalSeconds);

                try
                {
                    await Task.Delay(ReconnectDelay, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task ConsumeUntilShutdownAsync(
        string queue,
        string routingKey,
        Func<IntegrationEvent, CancellationToken, Task> handler,
        CancellationToken cancellationToken)
    {
        var connection = await _connectionProvider.GetConnectionAsync(cancellationToken);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await DeclareTopologyAsync(channel, queue, routingKey, cancellationToken);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: MessagingTopology.ConsumerPrefetch,
            global: false,
            cancellationToken: cancellationToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        channel.ChannelShutdownAsync += (_, args) =>
        {
            _logger.LogWarning(
                "Channel for '{Queue}' was shut down ({ReplyCode} {ReplyText}).",
                queue,
                args.ReplyCode,
                args.ReplyText);

            stopped.TrySetResult();

            return Task.CompletedTask;
        };

        consumer.ReceivedAsync += async (_, delivery) =>
            await HandleDeliveryAsync(channel, queue, handler, delivery, cancellationToken);

        var consumerTag = await channel.BasicConsumeAsync(
            queue: queue,
            autoAck: false,
            consumerTag: string.Empty,
            noLocal: false,
            exclusive: false,
            arguments: null,
            consumer: consumer,
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Consuming '{Queue}' (routing key '{RoutingKey}') with tag '{ConsumerTag}'.",
            queue,
            routingKey,
            consumerTag);

        await stopped.Task.WaitAsync(cancellationToken);
    }

    private static async Task DeclareTopologyAsync(
        IChannel channel,
        string queue,
        string routingKey,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: MessagingTopology.InventoryExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: MessagingTopology.DeadLetterExchange,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        var deadLetterQueue = MessagingTopology.DeadLetterQueue(queue);

        await channel.QueueDeclareAsync(
            queue: deadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: deadLetterQueue,
            exchange: MessagingTopology.DeadLetterExchange,
            routingKey: "#",
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = MessagingTopology.DeadLetterExchange
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: queue,
            exchange: MessagingTopology.InventoryExchange,
            routingKey: routingKey,
            arguments: null,
            cancellationToken: cancellationToken);
    }

    private async Task HandleDeliveryAsync(
        IChannel channel,
        string queue,
        Func<IntegrationEvent, CancellationToken, Task> handler,
        BasicDeliverEventArgs delivery,
        CancellationToken cancellationToken)
    {
        if (!EventRegistry.TryResolve(delivery.RoutingKey, out _))
        {
            _logger.LogError(
                "Event '{RoutingKey}' is not registered; moving it to '{DeadLetterQueue}'.",
                delivery.RoutingKey,
                MessagingTopology.DeadLetterQueue(queue));

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);

            return;
        }

        try
        {
            var integrationEvent = EventSerializer.Deserialize(
                delivery.RoutingKey,
                Encoding.UTF8.GetString(delivery.Body.Span));

            await handler(integrationEvent, cancellationToken);

            await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Shutting down: leave the message unacknowledged so the broker
            // redelivers it to the next instance instead of losing it.
            _logger.LogInformation("Shutdown during '{RoutingKey}'; leaving it unacknowledged.", delivery.RoutingKey);
        }
        catch (Exception exception)
        {
            if (!delivery.Redelivered)
            {
                _logger.LogWarning(
                    exception,
                    "Handling '{RoutingKey}' (event {EventId}) failed; retrying once.",
                    delivery.RoutingKey,
                    delivery.BasicProperties.MessageId);

                await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: true, cancellationToken);

                return;
            }

            _logger.LogError(
                exception,
                "Handling '{RoutingKey}' (event {EventId}) failed twice; moving it to '{DeadLetterQueue}'.",
                delivery.RoutingKey,
                delivery.BasicProperties.MessageId,
                MessagingTopology.DeadLetterQueue(queue));

            await channel.BasicNackAsync(delivery.DeliveryTag, multiple: false, requeue: false, cancellationToken);
        }
    }
}
