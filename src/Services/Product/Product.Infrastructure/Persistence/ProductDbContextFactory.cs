using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventorySystem.Products.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI (`dotnet ef migrations ...`).
///
/// It deliberately pins the provider to SQL Server. Without it, the CLI would
/// pick up appsettings.Development.json (the SQLite fallback) and emit
/// SQLite-flavoured migrations — wrong column types and no IDENTITY — which
/// would then fail against the real SQL Server database.
/// </summary>
public class ProductDbContextFactory : IDesignTimeDbContextFactory<ProductDbContext>
{
    public ProductDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ProductDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=ProductDb;Trusted_Connection=True;TrustServerCertificate=True",
            sqlServer => sqlServer.MigrationsAssembly(typeof(ProductDbContext).Assembly.FullName));

        return new ProductDbContext(optionsBuilder.Options);
    }
}
