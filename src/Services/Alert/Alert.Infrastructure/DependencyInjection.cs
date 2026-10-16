using InventorySystem.Alerts.Application.Abstractions;
using InventorySystem.Alerts.Infrastructure.Messaging;
using InventorySystem.Alerts.Infrastructure.Persistence;
using InventorySystem.Alerts.Infrastructure.Persistence.Repositories;
using InventorySystem.BuildingBlocks.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.Alerts.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAlertInfrastructure(this IServiceCollection services)
    {
        // Resolved lazily so the final configuration (including test overrides) is
        // applied when AlertDbContext is first constructed.
        services.TryAddSingleton(serviceProvider => AlertDatabaseOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<AlertDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<AlertDatabaseOptions>();

            if (options.IsSqlite)
            {
                builder.UseSqlite(options.ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(AlertDbContext).Assembly.FullName));
            }
            else
            {
                builder.UseSqlServer(options.ConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(AlertDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IAlertRepository, AlertRepository>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // RabbitMQ transport shared with the Inventory service, plus the background
        // consumer that projects inventory events into AlertDb.
        services.AddRabbitMqMessaging();

        // The consumer checks "RabbitMq:Enabled" itself: with no broker it does not
        // start, so the API still runs and no connection is attempted.
        services.AddHostedService<InventoryEventsConsumer>();

        return services;
    }
}
