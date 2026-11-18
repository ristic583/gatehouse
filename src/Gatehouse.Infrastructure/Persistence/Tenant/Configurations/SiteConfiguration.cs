using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("sites");
        builder.HasKey(s => s.Id);
        builder.Ignore(s => s.DomainEvents);
        builder.Property(s => s.Name).HasMaxLength(100);
        builder.Property(s => s.Code).HasMaxLength(20);
        builder.Property(s => s.TimeZoneId).HasMaxLength(64);
        builder.HasIndex(s => s.Name).IsUnique();
        builder.HasIndex(s => s.Code).IsUnique();
    }
}
