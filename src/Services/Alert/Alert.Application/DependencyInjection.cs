using InventorySystem.Alerts.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Alerts.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddAlertApplication(this IServiceCollection services)
    {
        services.AddScoped<AlertService>();
        services.AddScoped<InventoryEventProcessor>();

        return services;
    }
}
