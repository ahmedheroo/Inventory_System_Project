using InventorySystem.Alerts.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Alerts.Infrastructure.Persistence.Configurations;

public class AlertConfiguration : IEntityTypeConfiguration<Alert>
{
    public void Configure(EntityTypeBuilder<Alert> builder)
    {
        builder.ToTable("Alerts");

        builder.HasKey(alert => alert.Id);

        builder.Property(alert => alert.ProductId).IsRequired();

        builder.Property(alert => alert.WarehouseId).IsRequired();

        builder.Property(alert => alert.Type)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(alert => alert.Message)
            .IsRequired()
            .HasMaxLength(Alert.MessageMaxLength);

        builder.Property(alert => alert.CurrentQuantity).IsRequired();

        builder.Property(alert => alert.MinimumStockLevel).IsRequired();

        builder.Property(alert => alert.IsRead).IsRequired();

        builder.Property(alert => alert.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(alert => alert.CreatedAt).IsRequired();

        builder.Property(alert => alert.UpdatedAt).IsRequired();

        // The consumer's only lookup: is there already an active alert for this
        // (product, warehouse) pair?
        builder.HasIndex(alert => new { alert.ProductId, alert.WarehouseId, alert.Status })
            .HasDatabaseName("IX_Alerts_Product_Warehouse_Status");

        // The list endpoint filters on status/read state and orders by newest first.
        builder.HasIndex(alert => new { alert.Status, alert.IsRead, alert.CreatedAt })
            .HasDatabaseName("IX_Alerts_Status_IsRead_CreatedAt");
    }
}
