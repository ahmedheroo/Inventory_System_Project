using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventorySystem.Alerts.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI. It pins the provider to SQL
/// Server so migrations are never generated against the SQLite development
/// fallback (which would emit TEXT columns and no IDENTITY).
/// </summary>
public class AlertDbContextFactory : IDesignTimeDbContextFactory<AlertDbContext>
{
    public AlertDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AlertDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=AlertDb;Trusted_Connection=True;TrustServerCertificate=True",
            sqlServer => sqlServer.MigrationsAssembly(typeof(AlertDbContext).Assembly.FullName));

        return new AlertDbContext(optionsBuilder.Options);
    }
}
