using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace InventorySystem.Reports.Infrastructure.Persistence;

public static class ReportingDbInitializer
{
    /// <summary>
    /// Applies migrations (SQL Server) or creates the schema directly (SQLite dev
    /// fallback).
    ///
    /// No seed data: the read model is a projection of inventory events, so an
    /// empty ReportingDb is the correct starting state — rows appear once the
    /// Inventory service starts publishing and this service consumes.
    /// </summary>
    public static async Task InitializeAsync(
        ReportingDbContext dbContext,
        ReportingDatabaseOptions options,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (options.ApplyMigrations)
        {
            logger.LogInformation("Applying ReportingDb migrations ({Provider})...", options.Provider);
            await dbContext.Database.MigrateAsync(cancellationToken);
        }
        else
        {
            logger.LogInformation("Ensuring ReportingDb schema exists ({Provider} dev fallback)...", options.Provider);
            await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        }

        logger.LogInformation("ReportingDb is ready.");
    }
}
