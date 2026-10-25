using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Identity.Infrastructure.Persistence;

public static class IdentityDbInitializer
{
 
    public static async Task InitializeAsync(
        IdentityDbContext dbContext,
        IdentityDatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (options.ApplyMigrations)
        {
            logger.LogInformation("Applying IdentityDb migrations ({Provider})...", options.Provider);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring IdentityDb schema exists ({Provider} dev fallback)...", options.Provider);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        logger.LogInformation("IdentityDb is ready.");
    }
}
