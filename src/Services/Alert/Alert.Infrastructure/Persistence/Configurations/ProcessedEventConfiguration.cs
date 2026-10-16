using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Alerts.Infrastructure.Persistence.Configurations;

public class ProcessedEventConfiguration : IEntityTypeConfiguration<ProcessedEvent>
{
    public void Configure(EntityTypeBuilder<ProcessedEvent> builder)
    {
        builder.ToTable("ProcessedEvents");

        // The event id is the key: the database itself refuses to record the same
        // event twice, which is the last line of defence for idempotency.
        builder.HasKey(processed => processed.EventId);

        builder.Property(processed => processed.EventId).ValueGeneratedNever();

        builder.Property(processed => processed.EventName)
            .IsRequired()
            .HasMaxLength(100);

        builder.Property(processed => processed.ProcessedAt).IsRequired();
    }
}
