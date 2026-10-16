using Microsoft.Extensions.Configuration;

namespace InventorySystem.Products.Infrastructure.Persistence;

/// <summary>
/// Resolves which database provider ProductDb runs on.
///
/// The specification targets SQL Server. Because the Docker environment is not
/// always available on a developer machine, SQLite can be selected as a local
/// fallback (see appsettings.Development.json). SQLite has no SQL
/// Server migrations, so the schema is created directly instead.
/// </summary>
public sealed record ProductDatabaseOptions(string Provider, string ConnectionString)
{
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public bool IsSqlite => string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);

    public bool ApplyMigrations => !IsSqlite;

    public static ProductDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqlServerProvider;
        }

        var connectionString = configuration.GetConnectionString("ProductDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'ProductDb' is not configured (ConnectionStrings:ProductDb).");
        }

        return new ProductDatabaseOptions(provider, connectionString);
    }
}
