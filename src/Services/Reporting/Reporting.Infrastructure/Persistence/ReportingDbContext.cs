using InventorySystem.Reports.Application.ReadModel;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Reports.Infrastructure.Persistence;

/// <summary>
/// ReportingDb — owned exclusively by the Reporting service (spec section 10). It
/// holds a read model, not business data: one row per (product, warehouse) stock
/// level, the append-only movement ledger, and the ledger of consumed events that
/// makes the projection idempotent.
/// </summary>
public class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options) : base(options)
    {
    }

    /// <summary>Current quantity per (product, warehouse) — the inventory report.</summary>
    public DbSet<StockLevelSnapshot> StockLevels => Set<StockLevelSnapshot>();

    /// <summary>Append-only movement history — the movement report.</summary>
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    /// <summary>De-duplication ledger of the consumed inventory events.</summary>
    public DbSet<ProcessedEvent> ProcessedEvents => Set<ProcessedEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ReportingDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
