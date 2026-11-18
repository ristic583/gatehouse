using Gatehouse.Domain.Common;
using Gatehouse.Domain.Platform;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class TenantStatisticsConfiguration : IEntityTypeConfiguration<TenantStatistics>
{
    public void Configure(EntityTypeBuilder<TenantStatistics> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenant_statistics");
        builder.HasKey(s => s.TenantId);
        builder.Property(s => s.TenantId)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.HasOne<Domain.Tenancy.Tenant>().WithOne().HasForeignKey<TenantStatistics>(s => s.TenantId);
    }
}
