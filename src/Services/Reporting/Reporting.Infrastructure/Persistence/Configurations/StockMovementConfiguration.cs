using InventorySystem.Reports.Application.ReadModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Reports.Infrastructure.Persistence.Configurations;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");

        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.ProductId).IsRequired();

        builder.Property(movement => movement.WarehouseId).IsRequired();

        builder.Property(movement => movement.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(movement => movement.Quantity).IsRequired();

        builder.Property(movement => movement.QuantityAfter).IsRequired();

        builder.Property(movement => movement.EventId).IsRequired();

        builder.Property(movement => movement.OccurredAt).IsRequired();

        // The movement report orders by time within a warehouse/product and filters
        // on the date range (TASK-062) and the warehouse (TASK-063).
        builder.HasIndex(movement => new { movement.WarehouseId, movement.OccurredAt })
            .HasDatabaseName("IX_StockMovements_Warehouse_OccurredAt");

        builder.HasIndex(movement => new { movement.ProductId, movement.OccurredAt })
            .HasDatabaseName("IX_StockMovements_Product_OccurredAt");

        // A second, database-level guard against a redelivered event: one event can
        // have at most one movement of a given type in a given warehouse (a
        // transfer contributes TRANSFER_OUT and TRANSFER_IN to two warehouses).
        builder.HasIndex(movement => new { movement.EventId, movement.Type, movement.WarehouseId })
            .IsUnique()
            .HasDatabaseName("IX_StockMovements_Event_Type_Warehouse");
    }
}
