using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ReconciliationReportConfiguration : IEntityTypeConfiguration<ReconciliationReport>
{
    public void Configure(EntityTypeBuilder<ReconciliationReport> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("reconciliation_reports");
        builder.HasKey(r => r.Id);
        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.IsRunning);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.FailureCode).HasMaxLength(100);
        builder.Property(r => r.FailureMessage).HasMaxLength(2000);
        builder.Property(r => r.Coverage)
            .HasColumnType("jsonb")
            .HasConversion(
                JsonValueConversions.ObjectConverter<ReconciliationCoverage?>(),
                JsonValueConversions.ObjectComparer<ReconciliationCoverage?>());
        builder.Property(r => r.Version).IsRowVersion();
        builder.HasOne<PacsInstance>().WithMany().HasForeignKey(r => r.PacsInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany<ReconciliationFinding>().WithOne().HasForeignKey(f => f.ReportId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany<ReconciliationExportRecord>().WithOne().HasForeignKey(e => e.ReportId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.PacsInstanceId, r.RequestedAt }).IsDescending(false, true);
    }
}
