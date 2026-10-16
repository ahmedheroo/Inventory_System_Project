using InventorySystem.Products.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Products.Infrastructure.Persistence.Configurations;

public class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("Warehouses");

        builder.HasKey(warehouse => warehouse.Id);

        builder.Property(warehouse => warehouse.Code)
            .IsRequired()
            .HasMaxLength(Warehouse.CodeMaxLength);

        builder.HasIndex(warehouse => warehouse.Code)
            .IsUnique()
            .HasDatabaseName("IX_Warehouses_Code");

        builder.Property(warehouse => warehouse.Name)
            .IsRequired()
            .HasMaxLength(Warehouse.NameMaxLength);

        builder.Property(warehouse => warehouse.Address)
            .HasMaxLength(500);

        builder.Property(warehouse => warehouse.IsActive)
            .IsRequired();
    }
}
