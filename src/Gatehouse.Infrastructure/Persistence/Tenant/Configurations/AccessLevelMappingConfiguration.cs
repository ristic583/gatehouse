using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class AccessLevelMappingConfiguration : IEntityTypeConfiguration<AccessLevelMapping>
{
    public void Configure(EntityTypeBuilder<AccessLevelMapping> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("access_level_mappings");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.VendorAccessLevelRef).HasMaxLength(200);
        builder.Property(m => m.DisplayName).HasMaxLength(200);
        builder.HasOne<Area>().WithMany().HasForeignKey(m => m.AreaId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PacsInstance>().WithMany().HasForeignKey(m => m.PacsInstanceId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(m => new { m.AreaId, m.PacsInstanceId }).IsUnique();
    }
}
