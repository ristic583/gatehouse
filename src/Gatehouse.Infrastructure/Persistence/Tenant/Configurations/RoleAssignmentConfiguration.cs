using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class RoleAssignmentConfiguration : IEntityTypeConfiguration<RoleAssignment>
{
    public void Configure(EntityTypeBuilder<RoleAssignment> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("role_assignments");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Role).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Person>().WithMany().HasForeignKey(r => r.PersonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Site>().WithMany().HasForeignKey(r => r.SiteId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(r => new { r.PersonId, r.Role, r.SiteId }).IsUnique();
    }
}
