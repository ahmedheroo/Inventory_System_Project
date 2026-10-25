using InventorySystem.Identity.Application.Services;
using Microsoft.Extensions.DependencyInjection;

namespace InventorySystem.Identity.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityApplication(this IServiceCollection services)
    {
        services.AddScoped<SignInService>();
        services.AddScoped<UserDirectory>();

        return services;
    }
}
