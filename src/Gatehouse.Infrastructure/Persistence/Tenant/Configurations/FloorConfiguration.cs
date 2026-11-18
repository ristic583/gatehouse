using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class FloorConfiguration : IEntityTypeConfiguration<Floor>
{
    public void Configure(EntityTypeBuilder<Floor> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("floors");
        builder.HasKey(f => f.Id);
        builder.Ignore(f => f.DomainEvents);
        builder.Property(f => f.Name).HasMaxLength(100);
        builder.HasOne<Site>().WithMany().HasForeignKey(f => f.SiteId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => new { f.SiteId, f.Level }).IsUnique();
        builder.Ignore(f => f.HasBackground);
        builder.Property(f => f.BackgroundObjectKey).HasMaxLength(300);
        builder.Property(f => f.BackgroundContentType).HasMaxLength(50);
    }
}
