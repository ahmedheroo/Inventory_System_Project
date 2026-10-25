using InventorySystem.Reports.Application.ReadModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Reports.Infrastructure.Persistence.Configurations;

public class StockLevelSnapshotConfiguration : IEntityTypeConfiguration<StockLevelSnapshot>
{
    public void Configure(EntityTypeBuilder<StockLevelSnapshot> builder)
    {
        builder.ToTable("StockLevels");

        builder.HasKey(stockLevel => stockLevel.Id);

        builder.Property(stockLevel => stockLevel.ProductId).IsRequired();

        builder.Property(stockLevel => stockLevel.WarehouseId).IsRequired();

        builder.Property(stockLevel => stockLevel.Quantity).IsRequired();

        builder.Property(stockLevel => stockLevel.MinimumStockLevel).IsRequired();

        builder.Property(stockLevel => stockLevel.SourceEventId).IsRequired();

        builder.Property(stockLevel => stockLevel.UpdatedAt).IsRequired();

        // One row per pair is what makes the projection an upsert rather than a
        // log, and the unique index enforces it in the database as well.
        builder.HasIndex(stockLevel => new { stockLevel.ProductId, stockLevel.WarehouseId })
            .IsUnique()
            .HasDatabaseName("IX_StockLevels_Product_Warehouse");

        // The warehouse filter of both reports, and the valuation grouping.
        builder.HasIndex(stockLevel => stockLevel.WarehouseId)
            .HasDatabaseName("IX_StockLevels_Warehouse");
    }
}
