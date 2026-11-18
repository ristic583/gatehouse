using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class TenantProvisioningConfiguration : IEntityTypeConfiguration<TenantProvisioning>
{
    public void Configure(EntityTypeBuilder<TenantProvisioning> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenant_provisioning");
        builder.HasKey(p => p.TenantId);
        builder.Property(p => p.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.HasOne<Domain.Tenancy.Tenant>().WithOne().HasForeignKey<TenantProvisioning>(p => p.TenantId);
        builder.Property(p => p.Step).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.State).HasConversion<string>().HasMaxLength(32);
        builder.Property(p => p.LastError).HasMaxLength(TenantProvisioning.LastErrorMaxLength);
        builder.Property(p => p.AdminEmail).HasMaxLength(254).IsRequired();
        builder.Property(p => p.AdminGivenName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.AdminFamilyName).HasMaxLength(100).IsRequired();
        builder.Property(p => p.TimeZoneId).HasMaxLength(64).IsRequired();
    }
}
