using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class SecurityLevelConfiguration : IEntityTypeConfiguration<SecurityLevel>
{
    public void Configure(EntityTypeBuilder<SecurityLevel> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("security_levels");
        builder.HasKey(l => l.Id);
        builder.Ignore(l => l.DomainEvents);
        builder.Property(l => l.Name).HasMaxLength(100);
        builder.Property(l => l.ApproverChain).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(l => l.Name).IsUnique();
        builder.HasIndex(l => l.Rank).IsUnique();
    }
}
