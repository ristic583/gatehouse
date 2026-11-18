using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class PacsInstanceConfiguration : IEntityTypeConfiguration<PacsInstance>
{
    public void Configure(EntityTypeBuilder<PacsInstance> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("pacs_instances");
        builder.HasKey(p => p.Id);
        builder.Ignore(p => p.DomainEvents);
        builder.Property(p => p.Name).HasMaxLength(100);
        builder.Property(p => p.Vendor).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.FulfillmentMode).HasConversion<string>().HasMaxLength(20).HasDefaultValue(FulfillmentMode.Manual);
        builder.Property(p => p.AgentVersion).HasMaxLength(50);
        builder.Property(p => p.CapabilitiesJson).HasColumnName("capabilities").HasColumnType("jsonb");
        builder.Property(p => p.SiteIds)
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConversions.ListConverter<SiteId>(), JsonValueConversions.ListComparer<SiteId>());
        builder.HasIndex(p => p.Name).IsUnique();
    }
}
