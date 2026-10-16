using InventorySystem.Stock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Stock.Infrastructure.Persistence.Configurations;

public class InventoryConfiguration : IEntityTypeConfiguration<Inventory>
{
    public void Configure(EntityTypeBuilder<Inventory> builder)
    {
        builder.ToTable("Inventory");

        builder.HasKey(inventory => inventory.Id);

        builder.Property(inventory => inventory.ProductId).IsRequired();

        builder.Property(inventory => inventory.WarehouseId).IsRequired();

        builder.Property(inventory => inventory.Quantity).IsRequired();

        // One stock level per product and warehouse; this is also what stops two
        // concurrent operations from creating competing rows.
        builder.HasIndex(inventory => new { inventory.ProductId, inventory.WarehouseId })
            .IsUnique()
            .HasDatabaseName("IX_Inventory_ProductId_WarehouseId");

        // Optimistic concurrency: UpdatedAt is part of the UPDATE's WHERE clause,
        // so an operation acting on a stale quantity fails instead of silently
        // overwriting a newer value (e.g. two simultaneous issues over-issuing).
        builder.Property(inventory => inventory.UpdatedAt)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
