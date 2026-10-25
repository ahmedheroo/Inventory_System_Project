using InventorySystem.BuildingBlocks.Security;
using InventorySystem.Identity.Application.Abstractions;
using InventorySystem.Identity.Domain.Security;
using InventorySystem.Identity.Infrastructure.Persistence;
using InventorySystem.Identity.Infrastructure.Persistence.Repositories;
using InventorySystem.Identity.Infrastructure.Seeding;
using InventorySystem.Identity.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace InventorySystem.Identity.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services)
    {
        // Resolved lazily so the final configuration (including test overrides) is applied when
        // IdentityDbContext is first constructed.
        services.TryAddSingleton(serviceProvider => IdentityDatabaseOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddDbContext<IdentityDbContext>((serviceProvider, builder) =>
        {
            var options = serviceProvider.GetRequiredService<IdentityDatabaseOptions>();

            if (options.IsSqlite)
            {
                builder.UseSqlite(options.ConnectionString, sqlite =>
                    sqlite.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName));
            }
            else
            {
                builder.UseSqlServer(options.ConnectionString, sqlServer =>
                {
                    sqlServer.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName);
                    sqlServer.EnableRetryOnFailure(3);
                });
            }
        });

        // One hasher for the process: the decoy hash it keeps for refusals is computed once.
        services.TryAddSingleton(new PasswordHasher());

        services.TryAddSingleton(TimeProvider.System);

        // The signing side of the same options the services validate with. AddInventoryJwtAuthentication
        // registers it too; whichever runs first wins, and both produce the same values.
        services.TryAddSingleton(serviceProvider => JwtOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.TryAddSingleton(serviceProvider => new JwtTokenFactory(
            serviceProvider.GetRequiredService<JwtOptions>()));

        services.TryAddSingleton<ITokenIssuer, IdentityTokenIssuer>();

        services.TryAddSingleton(serviceProvider => IdentitySeedOptions.FromConfiguration(
            serviceProvider.GetRequiredService<IConfiguration>()));

        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IdentitySeeder>();

        return services;
    }
}
