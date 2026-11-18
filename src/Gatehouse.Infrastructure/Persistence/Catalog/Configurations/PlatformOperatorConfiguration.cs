using Gatehouse.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class PlatformOperatorConfiguration : IEntityTypeConfiguration<PlatformOperator>
{
    public void Configure(EntityTypeBuilder<PlatformOperator> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("platform_operators");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id)
            .HasConversion(id => id.Value, value => new PlatformOperatorId(value))
            .ValueGeneratedNever();
        builder.Property(o => o.Email).HasMaxLength(254).IsRequired();
        builder.Property(o => o.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(o => o.Subject).HasMaxLength(128);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(o => o.Email).IsUnique();
        builder.HasIndex(o => o.Subject).IsUnique().HasFilter("subject IS NOT NULL");
        builder.Ignore(o => o.DomainEvents);
    }
}
