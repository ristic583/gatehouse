using Gatehouse.Domain.Audit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("audit_entries");
        builder.HasKey(a => a.Sequence);
        builder.Property(a => a.Sequence).ValueGeneratedNever();
        builder.Property(a => a.ActorKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(a => a.ActorId).HasMaxLength(100);
        builder.Property(a => a.Action).HasMaxLength(100);
        builder.Property(a => a.SubjectType).HasMaxLength(50);
        builder.Property(a => a.SubjectId).HasMaxLength(100);
        builder.Property(a => a.Payload).HasColumnType("jsonb");
        builder.Property(a => a.PrevHash).HasColumnType("char(64)");
        builder.Property(a => a.Hash).HasColumnType("char(64)");
        builder.Ignore(a => a.LocalOrder);
        builder.HasIndex(a => new { a.SubjectType, a.SubjectId });
    }
}
