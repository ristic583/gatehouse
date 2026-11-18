using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ReconciliationExportRecordConfiguration : IEntityTypeConfiguration<ReconciliationExportRecord>
{
    public void Configure(EntityTypeBuilder<ReconciliationExportRecord> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("reconciliation_export_records");
        builder.HasKey(e => new { e.ReportId, e.ChunkIndex, e.Ordinal });
        builder.Property(e => e.RecordType).HasMaxLength(30);
        builder.Property(e => e.PacsRef).HasMaxLength(200);
        builder.Property(e => e.PersonRef).HasMaxLength(200);
        builder.Property(e => e.CardNumber).HasMaxLength(64);
        builder.Property(e => e.VendorAccessLevelRef).HasMaxLength(200);
    }
}
