using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Catalog.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Domain.Tenancy.Tenant>
{
    public void Configure(EntityTypeBuilder<Domain.Tenancy.Tenant> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasConversion(id => id.Value, value => new TenantId(value))
            .HasMaxLength(32)
            .ValueGeneratedNever();
        builder.Property(t => t.DisplayName).HasMaxLength(Domain.Tenancy.Tenant.DisplayNameMaxLength).IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.IdpKind).HasConversion<string>().HasMaxLength(32);
        builder.Property(t => t.DatabaseName).HasMaxLength(63).IsRequired();
        builder.Property(t => t.EncryptedConnectionString).IsRequired();
        builder.Property(t => t.KeycloakOrganizationAlias).HasMaxLength(32).IsRequired();
        builder.Property(t => t.CreatedAt);
        builder.Property(t => t.EmailDomain).HasMaxLength(253);
        builder.Property(t => t.SuspendedReason).HasMaxLength(500);
        builder.HasIndex(t => t.DatabaseName).IsUnique();
        builder.HasIndex(t => t.EmailDomain).IsUnique().HasFilter("email_domain IS NOT NULL");
        builder.Ignore(t => t.DomainEvents);
    }
}
