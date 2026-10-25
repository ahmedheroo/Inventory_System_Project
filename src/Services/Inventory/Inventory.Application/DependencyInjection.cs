using InventorySystem.Stock.Application.Abstractions;
using InventorySystem.Stock.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Stock.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddInventoryApplication(this IServiceCollection services)
    {
        services.AddScoped<InventoryService>();

        // An operation nobody named. A host that can tell (the API, from the request) registers
        // its own implementation afterwards and replaces this one.
        services.AddScoped<IStockChangeOrigin, UnspecifiedStockChangeOrigin>();

        return services;
    }
}
