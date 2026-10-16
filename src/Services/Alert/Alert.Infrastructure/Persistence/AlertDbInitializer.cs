using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Alerts.Infrastructure.Persistence;

public static class AlertDbInitializer
{
    /// <summary>
    /// Applies migrations (SQL Server) or creates the schema directly (SQLite dev
    /// fallback).
    ///
    /// No seed data: alerts are projections of inventory events, so an empty
    /// AlertDb is the correct starting state — the rows appear once the Inventory
    /// service starts publishing.
    /// </summary>
    public static async Task InitializeAsync(
        AlertDbContext dbContext,
        AlertDatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (options.ApplyMigrations)
        {
            logger.LogInformation("Applying AlertDb migrations ({Provider})...", options.Provider);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring AlertDb schema exists ({Provider} dev fallback)...", options.Provider);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        logger.LogInformation("AlertDb is ready.");
    }
}
