using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace InventorySystem.Identity.Infrastructure.Persistence;

public class IdentityDbContextFactory : IDesignTimeDbContextFactory<IdentityDbContext>
{
    public IdentityDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<IdentityDbContext>();

        optionsBuilder.UseSqlServer(
            "Server=(localdb)\\mssqllocaldb;Database=IdentityDb;Trusted_Connection=True;TrustServerCertificate=True",
            sqlServer => sqlServer.MigrationsAssembly(typeof(IdentityDbContext).Assembly.FullName));

        return new IdentityDbContext(optionsBuilder.Options);
    }
}
