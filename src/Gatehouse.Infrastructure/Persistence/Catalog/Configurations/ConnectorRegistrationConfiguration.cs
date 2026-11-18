using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class ConnectorRegistrationConfiguration : IEntityTypeConfiguration<ConnectorRegistration>
{
    public void Configure(EntityTypeBuilder<ConnectorRegistration> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("connector_registrations");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasConversion(id => id.Value, v => new ConnectorId(v)).ValueGeneratedNever();
        builder.Property(c => c.TenantId).HasConversion(id => id.Value, v => new TenantId(v)).HasMaxLength(32);
        builder.Property(c => c.PacsInstanceId).HasConversion(id => id.Value, v => new PacsInstanceId(v));
        builder.Property(c => c.DisplayName).HasMaxLength(100);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.EnrollmentTokenHash).HasMaxLength(64);
        builder.Property(c => c.CertificateThumbprint).HasMaxLength(64);
        builder.HasIndex(c => c.CertificateThumbprint).IsUnique().HasFilter("certificate_thumbprint IS NOT NULL");
        builder.HasIndex(c => new { c.TenantId, c.PacsInstanceId }).IsUnique().HasFilter("status <> 'Revoked'");
    }
}
