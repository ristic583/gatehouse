using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ReconciliationFindingConfiguration : IEntityTypeConfiguration<ReconciliationFinding>
{
    public void Configure(EntityTypeBuilder<ReconciliationFinding> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("reconciliation_findings");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(f => f.PacsRef).HasMaxLength(200);
        builder.Property(f => f.PersonRef).HasMaxLength(200);
        builder.Property(f => f.VendorAccessLevelRef).HasMaxLength(200);
        builder.Property(f => f.PacsCardNumber).HasMaxLength(64);
        builder.Property(f => f.Details)
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConversions.ObjectConverter<FindingDetails>(), JsonValueConversions.ObjectComparer<FindingDetails>());
        builder.HasIndex(f => f.ReportId);
        builder.HasIndex(f => f.StaffTaskId);
    }
}
