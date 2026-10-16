using InventorySystem.Products.Application.Abstractions;
using InventorySystem.Products.Infrastructure.Persistence;
using InventorySystem.Products.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.Products.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddProductInfrastructure(this IServiceCollection services)
    {
        // Resolved lazily: the final configuration (appsettings.{Environment}.json,
        // environment variables, test overrides) is only complete once the host is built.
        services.TryAddSingleton(serviceProvider => ProductDatabaseOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<ProductDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<ProductDatabaseOptions>();

            if (options.IsSqlite)
            {
                builder.UseSqlite(options.ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(ProductDbContext).Assembly.FullName));
            }
            else
            {
                builder.UseSqlServer(options.ConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(ProductDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(3);
                });
            }
        });

        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICategoryRepository, CategoryRepository>();
        services.AddScoped<IWarehouseRepository, WarehouseRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        return services;
    }
}
