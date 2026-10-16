using InventorySystem.Stock.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Stock.Infrastructure.Persistence.Configurations;

public class InventoryTransactionConfiguration : IEntityTypeConfiguration<InventoryTransaction>
{
    public void Configure(EntityTypeBuilder<InventoryTransaction> builder)
    {
        builder.ToTable("InventoryTransactions");

        builder.HasKey(transaction => transaction.Id);

        builder.Property(transaction => transaction.ProductId).IsRequired();

        builder.Property(transaction => transaction.WarehouseId).IsRequired();

        builder.Property(transaction => transaction.Type)
            .IsRequired()
            .HasConversion<int>();

        builder.Property(transaction => transaction.Quantity).IsRequired();

        builder.Property(transaction => transaction.Reference)
            .HasMaxLength(InventoryTransaction.ReferenceMaxLength);

        builder.Property(transaction => transaction.CreatedAt).IsRequired();

        // The history endpoint filters by product/warehouse and orders by time.
        builder.HasIndex(transaction => new { transaction.ProductId, transaction.CreatedAt })
            .HasDatabaseName("IX_InventoryTransactions_ProductId_CreatedAt");

        builder.HasIndex(transaction => new { transaction.WarehouseId, transaction.CreatedAt })
            .HasDatabaseName("IX_InventoryTransactions_WarehouseId_CreatedAt");

        builder.Ignore(transaction => transaction.SignedQuantity);
    }
}
