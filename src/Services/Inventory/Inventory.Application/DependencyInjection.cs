using InventorySystem.Stock.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Stock.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        services.AddScoped<InventoryService>();

        return services;
    }
}
