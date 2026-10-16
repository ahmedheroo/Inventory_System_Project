using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace InventorySystem.BuildingBlocks.Messaging;

/// <summary>
/// Owns the AMQP connection. Publisher and consumer both go through it so there
/// is exactly one connection per process (the broker multiplexes channels).
/// </summary>
public interface IRabbitMqConnectionProvider
{
    /// <summary>
    /// Returns an open connection, connecting on first use and reconnecting
    /// after a broker restart.
    /// </summary>
    Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default);
}

public sealed class RabbitMqConnectionProvider : IRabbitMqConnectionProvider, IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionProvider> _logger;

    private IConnection? _connection;

    public RabbitMqConnectionProvider(RabbitMqOptions options, ILogger<RabbitMqConnectionProvider> logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_connection is { IsOpen: true } open)
        {
            return open;
        }

        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_connection is { IsOpen: true } connected)
            {
                return connected;
            }

            if (_connection is not null)
            {
                await DisposeQuietlyAsync(_connection);
                _connection = null;
            }

            var factory = new ConnectionFactory
            {
                HostName = _options.HostName,
                Port = _options.Port,
                UserName = _options.UserName,
                Password = _options.Password,
                VirtualHost = _options.VirtualHost,
                ClientProvidedName = _options.ClientName ?? "inventory-system",
                RequestedConnectionTimeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds)
            };

            _logger.LogInformation(
                "Connecting to RabbitMQ at {Host}:{Port} (vhost {VirtualHost})...",
                _options.HostName,
                _options.Port,
                _options.VirtualHost);

            var connection = await factory.CreateConnectionAsync(
                _options.ClientName ?? "inventory-system",
                cancellationToken);

            connection.ConnectionShutdownAsync += (_, args) =>
            {
                _logger.LogWarning(
                    "RabbitMQ connection closed ({ReplyCode} {ReplyText}). The next publish will reconnect.",
                    args.ReplyCode,
                    args.ReplyText);

                return Task.CompletedTask;
            };

            _connection = connection;
            _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}.", _options.HostName, _options.Port);

            return connection;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await DisposeQuietlyAsync(_connection);
            _connection = null;
        }

        _gate.Dispose();
    }

    private async Task DisposeQuietlyAsync(IConnection connection)
    {
        try
        {
            await connection.DisposeAsync();
        }
        catch (Exception exception)
        {
            _logger.LogDebug(exception, "Ignoring an error while closing the RabbitMQ connection.");
        }
    }
}
