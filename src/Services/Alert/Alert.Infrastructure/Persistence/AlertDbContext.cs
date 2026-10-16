using InventorySystem.Alerts.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Alerts.Infrastructure.Persistence;

/// <summary>
/// AlertDb — owned exclusively by the Alert service (spec section 10). Products
/// and warehouses are referenced by id; their names are resolved by the MVC layer
/// through the Product service, never by a cross-database join.
/// </summary>
public class AlertDbContext : DbContext
{
    public AlertDbContext(DbContextOptions<AlertDbContext> options) : base(options)
    {
    }

    public DbSet<Alert> Alerts => Set<Alert>();

    /// <summary>De-duplication ledger of the consumed inventory events.</summary>
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AlertDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
