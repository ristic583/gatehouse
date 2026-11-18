using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class StaffTaskConfiguration : IEntityTypeConfiguration<StaffTask>
{
    public void Configure(EntityTypeBuilder<StaffTask> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("staff_tasks");
        builder.HasKey(t => t.Id);
        builder.Ignore(t => t.DomainEvents);
        builder.Property(t => t.Kind).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(t => t.ResolutionAction).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.SubjectType).HasMaxLength(50);
        builder.Property(t => t.ResolutionNote).HasMaxLength(500);
        builder.Property(t => t.Version).IsRowVersion();
        builder.HasIndex(t => new { t.Status, t.SiteId });
        builder.HasIndex(t => new { t.Kind, t.SubjectId }).IsUnique().HasFilter("status <> 'Resolved'");
    }
}
