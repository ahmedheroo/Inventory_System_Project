using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace InventorySystem.Products.Infrastructure.Persistence;

/// <summary>
/// ProductDb — owned exclusively by the Product service (spec section 10).
/// No other service reads this database directly.
/// </summary>
public class ProductDbContext : DbContext
{
    public ProductDbContext(DbContextOptions<ProductDbContext> options) : base(options)
    {
    }

    public DbSet<Product> Products => Set<Product>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProductDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
