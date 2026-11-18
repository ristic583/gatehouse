using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class TenantSettingConfiguration : IEntityTypeConfiguration<TenantSetting>
{
    public void Configure(EntityTypeBuilder<TenantSetting> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenant_settings");
        builder.HasKey(s => s.Key);
        builder.Property(s => s.Key).HasMaxLength(TenantSetting.KeyMaxLength);
        builder.Property(s => s.Value).IsRequired();
        builder.Property(s => s.UpdatedAt);
    }
}
