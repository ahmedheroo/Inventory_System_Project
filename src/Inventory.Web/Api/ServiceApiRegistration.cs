using Inventory.Web.Api.Contracts;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Inventory.Web.Api;

public static class ServiceApiRegistration
{
    /// <summary>
    /// Registers one typed client per service, each with its own base address and
    /// timeout. The clients are interfaces on purpose: the console's tests replace
    /// them with fakes and never need a running service.
    ///
    /// Timeouts are per service, because their latency is not the same: the
    /// Reporting service reads a catalogue and aggregates its read model on every
    /// request, while the others answer from a single table.
    /// </summary>
    public static IServiceCollection AddServiceApis(this IServiceCollection services, IConfiguration configuration)
    {
        var options = ServiceApiOptions.FromConfiguration(configuration);

        services.TryAddSingleton(options);

        services.AddHttpClient<ICatalogApi, CatalogApi>(client =>
        {
            client.BaseAddress = options.ProductApi;
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // The Inventory client is the one that carries the station: it is the only service whose
        // operations a live screen has to attribute to the terminal that performed them.
        services.AddTransient<StationHeaderHandler>();

        services.AddHttpClient<IInventoryApi, InventoryApi>(client =>
        {
            client.BaseAddress = options.InventoryApi;
            client.Timeout = TimeSpan.FromSeconds(15);
        })
        .AddHttpMessageHandler<StationHeaderHandler>();

        services.AddHttpClient<IAlertsApi, AlertsApi>(client =>
        {
            client.BaseAddress = options.AlertApi;
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.AddHttpClient<IReportingApi, ReportingApi>(client =>
        {
            client.BaseAddress = options.ReportingApi;
            client.Timeout = TimeSpan.FromSeconds(45);
        });

        // Accounts and tokens. Short timeout on purpose: somebody is standing at a sign-in form
        // waiting for this answer.
        services.AddHttpClient<IIdentityApi, IdentityApi>(client =>
        {
            client.BaseAddress = options.IdentityApi;
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        services.TryAddSingleton(ServiceCredentialOptions.FromConfiguration(configuration));

        return services;
    }
}
