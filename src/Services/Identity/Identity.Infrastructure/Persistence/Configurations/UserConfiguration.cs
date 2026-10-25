using InventorySystem.Identity.Domain.Entities;
using InventorySystem.Identity.Domain.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace InventorySystem.Identity.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");

        builder.HasKey(user => user.Id);

        // The sign-in key, and the one thing two accounts may not share. The unique index is what
        // actually holds when two registrations arrive at the same instant.
        builder.Property(user => user.Username)
            .IsRequired()
            .HasMaxLength(User.UsernameMaxLength);

        builder.HasIndex(user => user.Username)
            .IsUnique()
            .HasDatabaseName("IX_Users_Username");

        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(User.DisplayNameMaxLength);

        // Roles are stored as their names: a row read by hand says "manager", and renumbering the
        // enum can never silently change what an account is.
        builder.Property(user => user.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        // Only the encoded hash is ever written. The salt and the work factor travel inside it.
        builder.Property(user => user.Password)
            .HasConversion(hash => hash.Encoded, encoded => PasswordHash.Parse(encoded))
            .HasColumnName("PasswordHash")
            .HasMaxLength(400)
            .IsRequired();

        builder.Property(user => user.IsActive).IsRequired();

        builder.Property(user => user.CreatedAt).IsRequired();

        builder.Property(user => user.LastSignedInAt);
    }
}
