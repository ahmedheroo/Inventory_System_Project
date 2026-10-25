using InventorySystem.Reports.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Reports.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReportingApplication(this IServiceCollection services)
    {
        services.AddScoped<ReportService>();
        services.AddScoped<InventoryEventProjector>();

        return services;
    }
}
