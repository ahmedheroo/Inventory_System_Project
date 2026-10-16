using InventorySystem.BuildingBlocks.Messaging;
using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Infrastructure.Catalog;
using InventorySystem.Stock.Infrastructure.Messaging;
using InventorySystem.Stock.Infrastructure.Persistence;
using InventorySystem.Stock.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.Stock.Infrastructure;

public static class DependencyInjection
{
    private const string ProductApiClientName = "product-api";

    public static IServiceCollection AddInventoryInfrastructure(this IServiceCollection services)
    {
        // Resolved lazily so the final configuration (including test overrides)
        // is applied when InventoryDbContext is first constructed.
        services.TryAddSingleton(serviceProvider => InventoryDatabaseOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<InventoryDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<InventoryDatabaseOptions>();

            if (options.IsSqlite)
            {
                builder.UseSqlite(options.ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName));
            }
            else
            {
                builder.UseSqlServer(options.ConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IInventoryTransactionRepository, InventoryTransactionRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Transactional outbox: stock operations enqueue their event inside the
        // same transaction, the dispatcher delivers it to RabbitMQ afterwards.
        services.TryAddSingleton(serviceProvider => OutboxOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddRabbitMqMessaging();

        services.AddScoped<OutboxStore>();
        services.AddScoped<IEventOutbox>(serviceProvider => serviceProvider.GetRequiredService<OutboxStore>());
        services.AddSingleton<OutboxProcessor>();

        // The dispatcher is always registered and checks "RabbitMq:Enabled" itself:
        // with no broker (tests, or a machine without Docker) it does not start,
        // the events stay in the outbox and nothing is lost.
        services.AddHostedService<OutboxDispatcher>();

        // Products and warehouses belong to the Product service (spec section 10).
        services.AddHttpClient(ProductApiClientName, (serviceProvider, client) =>
        {
            var baseUrl = serviceProvider.GetRequiredService<IConfiguration>()["Services:ProductApi:BaseUrl"]
                          ?? "http://localhost:5251";

            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<IProductCatalogClient>(serviceProvider =>
            new HttpProductCatalogClient(
                serviceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(ProductApiClientName)));

        return services;
    }
}
