using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventorySystem.Reports.Infrastructure.Persistence;

/// <summary>
/// Design-time factory used by the EF Core CLI. It pins the provider to SQL
/// Server so migrations are never generated against the SQLite development
/// fallback (which would emit TEXT columns and no IDENTITY).
/// </summary>
public class ReportingDbContextFactory : IDesignTimeDbContextFactory<ReportingDbContext>
{
    public ReportingDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ReportingDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=ReportingDb;Trusted_Connection=True;TrustServerCertificate=True",
            sqlServer => sqlServer.MigrationsAssembly(typeof(ReportingDbContext).Assembly.FullName));

        return new ReportingDbContext(optionsBuilder.Options);
    }
}
