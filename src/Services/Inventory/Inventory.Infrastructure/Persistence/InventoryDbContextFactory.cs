using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventorySystem.Stock.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI. It pins the provider to SQL
/// Server so migrations are never generated against the SQLite development
/// fallback (which would emit TEXT columns and no IDENTITY).
/// </summary>
public class InventoryDbContextFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<InventoryDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=InventoryDb;Trusted_Connection=True;TrustServerCertificate=True",
            sqlServer => sqlServer.MigrationsAssembly(typeof(InventoryDbContext).Assembly.FullName));

        return new InventoryDbContext(optionsBuilder.Options);
    }
}
