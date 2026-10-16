using InventorySystem.Stock.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Stock.Infrastructure.Persistence;

/// <summary>
/// InventoryDb — owned exclusively by the Inventory service (spec section 10).
/// Products and warehouses are referenced by id only; their data lives in
/// ProductDb and is reached through the Product service's API.
/// </summary>
public class InventoryDbContext : DbContext
{
    public InventoryDbContext(DbContextOptions<InventoryDbContext> options) : base(options)
    {
    }

    /// <summary>Current stock: one row per (product, warehouse).</summary>
    public DbSet<Inventory> StockLevels => Set<Inventory>();

    /// <summary>Append-only stock ledger backing the history endpoint.</summary>
    public DbSet<InventoryTransaction> Transactions => Set<InventoryTransaction>();

    /// <summary>Stock events awaiting delivery to the message broker (transactional outbox).</summary>
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
