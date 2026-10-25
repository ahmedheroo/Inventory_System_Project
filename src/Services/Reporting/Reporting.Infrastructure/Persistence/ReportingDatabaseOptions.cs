using Microsoft.Extensions.Configuration;

namespace InventorySystem.Reports.Infrastructure.Persistence;

/// <summary>
/// Resolves which database provider ReportingDb runs on. Mirrors the Product,
/// Inventory and Alert services: SQL Server per the specification, with SQLite
/// available as a local development fallback (see appsettings.Development.json).
/// </summary>
public sealed record ReportingDatabaseOptions(string Provider, string ConnectionString)
{
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public bool IsSqlite => string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);

    public bool ApplyMigrations => !IsSqlite;

    public static ReportingDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqlServerProvider;
        }

        var connectionString = configuration.GetConnectionString("ReportingDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ReportingDb' is not configured (ConnectionStrings:ReportingDb).");
        }

        return new ReportingDatabaseOptions(provider, connectionString);
    }
}
