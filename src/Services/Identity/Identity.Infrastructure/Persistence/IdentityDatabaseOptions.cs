using Microsoft.Extensions.Configuration;

namespace InventorySystem.Identity.Infrastructure.Persistence;
 
public sealed record IdentityDatabaseOptions(string Provider, string ConnectionString)
{
    public const string SqlServerProvider = "SqlServer";
    public const string SqliteProvider = "Sqlite";

    public bool IsSqlite => string.Equals(Provider, SqliteProvider, StringComparison.OrdinalIgnoreCase);

    public bool ApplyMigrations => !IsSqlite;

    public static IdentityDatabaseOptions FromConfiguration(IConfiguration configuration)
    {
        var provider = configuration["Database:Provider"];
        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = SqlServerProvider;
        }

        var connectionString = configuration.GetConnectionString("IdentityDb");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Connection string 'IdentityDb' is not configured (ConnectionStrings:IdentityDb).");
        }

        return new IdentityDatabaseOptions(provider, connectionString);
    }
}
