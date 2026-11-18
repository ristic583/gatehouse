using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class PlatformAuditEntryConfiguration : IEntityTypeConfiguration<PlatformAuditEntry>
{
    public void Configure(EntityTypeBuilder<PlatformAuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("platform_audit_entries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(16);
        builder.Property(e => e.ActorId).HasMaxLength(128).IsRequired();
        builder.Property(e => e.Action).HasMaxLength(64).IsRequired();
        builder.Property(e => e.TenantId)
            .HasConversion(id => id!.Value.Value, value => new TenantId(value))
            .HasMaxLength(32);
        builder.Property(e => e.Data).HasColumnType("jsonb").IsRequired();
        builder.Property(e => e.PreviousHash).HasMaxLength(64).IsRequired();
        builder.Property(e => e.Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(e => e.Sequence).IsUnique();
    }
}
