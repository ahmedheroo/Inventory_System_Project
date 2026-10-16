using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Stock.Infrastructure.Persistence;

public static class InventoryDbInitializer
{
    /// <summary>
    /// Applies migrations (SQL Server) or creates the schema directly (SQLite dev fallback).
    ///
    /// No seed data: stock only becomes meaningful once real products and
    /// warehouses exist, and those are created through the Product service.
    /// </summary>
    public static async Task InitializeAsync(
        InventoryDbContext dbContext,
        InventoryDatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (options.ApplyMigrations)
        {
            logger.LogInformation("Applying InventoryDb migrations ({Provider})...", options.Provider);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring InventoryDb schema exists ({Provider} dev fallback)...", options.Provider);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        logger.LogInformation("InventoryDb is ready.");
    }
}
