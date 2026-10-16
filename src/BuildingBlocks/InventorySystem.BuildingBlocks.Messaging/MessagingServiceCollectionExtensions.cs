using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.BuildingBlocks.Messaging;

public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the RabbitMQ transport (options, connection, publisher and
    /// consumer) for a service that talks to the inventory exchange.
    ///
    /// Registering the call is not enough to make a service connect: the
    /// connection is opened lazily on the first publish or consume, so a service
    /// whose broker is down still starts and serves HTTP traffic.
    /// </summary>
    public static IServiceCollection AddRabbitMqMessaging(this IServiceCollection services)
    {
        // Resolved lazily so the final configuration (including test overrides) is
        // applied when the options are first requested.
        services.TryAddSingleton(serviceProvider => RabbitMqOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.TryAddSingleton<IRabbitMqConnectionProvider, RabbitMqConnectionProvider>();
        services.TryAddSingleton<IEventPublisher, RabbitMqEventPublisher>();
        services.TryAddSingleton<IEventConsumer, RabbitMqEventConsumer>();

        return services;
    }
}
