using Microsoft.Extensions.Configuration;

namespace InventorySystem.Alerts.Infrastructure.Persistence;

/// <summary>
/// Resolves which database provider AlertDb runs on. Mirrors the Product and
/// Inventory services: SQL Server per the specification, with SQLite available as
/// a local development fallback (see appsettings.Development.json).
/// </summary>
public sealed record AlertDatabaseOptions(string Provider, string ConnectionString)
{
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public bool IsSqlite => string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);

    public bool ApplyMigrations => !IsSqlite;

    public static AlertDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqlServerProvider;
        }

        var connectionString = configuration.GetConnectionString("AlertDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'AlertDb' is not configured (ConnectionStrings:AlertDb).");
        }

        return new AlertDatabaseOptions(provider, connectionString);
    }
}
