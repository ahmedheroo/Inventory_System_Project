using InventorySystem.Products.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Products.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddProductApplication(this IServiceCollection services)
    {
        services.AddScoped<ProductService>();
        services.AddScoped<CategoryService>();
        services.AddScoped<WarehouseService>();

        return services;
    }
}
