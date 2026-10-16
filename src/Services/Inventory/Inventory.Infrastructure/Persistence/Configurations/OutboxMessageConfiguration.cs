using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Stock.Infrastructure.Persistence.Configurations;

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");

        builder.HasKey(message => message.Id);

        // Assigned by the publisher (the event id), never by the database.
        builder.Property(message => message.Id).ValueGeneratedNever();

        builder.Property(message => message.EventName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(message => message.Payload)
            .IsRequired();

        builder.Property(message => message.OccurredAt).IsRequired();

        builder.Property(message => message.NextAttemptAt).IsRequired();

        builder.Property(message => message.LastError)
            .HasMaxLength(OutboxMessage.MaxErrorLength);

        // The dispatcher's only query: oldest pending message first.
        builder.HasIndex(message => new { message.ProcessedAt, message.NextAttemptAt })
            .HasDatabaseName("IX_OutboxMessages_Pending");
    }
}
