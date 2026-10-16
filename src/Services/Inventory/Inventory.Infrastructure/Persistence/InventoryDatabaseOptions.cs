using Microsoft.Extensions.Configuration;

namespace InventorySystem.Stock.Infrastructure.Persistence;

/// <summary>
/// Resolves which database provider InventoryDb runs on. Mirrors the Product
/// service: SQL Server per the specification, with SQLite available as a local
/// development fallback (see appsettings.Development.json).
/// </summary>
public sealed record InventoryDatabaseOptions(string Provider, string ConnectionString)
{
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public bool IsSqlite => string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);

    public bool ApplyMigrations => !IsSqlite;

    public static InventoryDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqlServerProvider;
        }

        var connectionString = configuration.GetConnectionString("InventoryDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'InventoryDb' is not configured (ConnectionStrings:InventoryDb).");
        }

        return new InventoryDatabaseOptions(provider, connectionString);
    }
}
