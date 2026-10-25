using InventorySystem.BuildingBlocks.Messaging;
using InventorySystem.Reports.Application.Abstractions;
using InventorySystem.Reports.Infrastructure.Catalog;
using InventorySystem.Reports.Infrastructure.Messaging;
using InventorySystem.Reports.Infrastructure.Persistence;
using InventorySystem.Reports.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.Reports.Infrastructure;

public static class DependencyInjection
{
    private const string ProductApiClientName = "product-api";

    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services)
    {
        // Resolved lazily so the final configuration (including test overrides) is
        // applied when ReportingDbContext is first constructed.
        services.TryAddSingleton(serviceProvider => ReportingDatabaseOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<ReportingDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<ReportingDatabaseOptions>();

            if (options.IsSqlite)
            {
                builder.UseSqlite(options.ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ReportingDbContext).Assembly.FullName));
            }
            else
            {
                builder.UseSqlServer(options.ConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(ReportingDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IReportingRepository, ReportingRepository>();
        services.AddScoped<IProcessedEventStore, ProcessedEventStore>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Products, warehouses and prices belong to the Product service (spec section 10).
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

        // RabbitMQ transport shared with the Inventory and Alert services, plus the
        // background consumer that projects inventory events into ReportingDb.
        services.AddRabbitMqMessaging();

        // The consumer checks "RabbitMq:Enabled" itself: with no broker it does not
        // start, so the API still runs and reports whatever the read model holds.
        services.AddHostedService<InventoryEventsConsumer>();

        return services;
    }
}
