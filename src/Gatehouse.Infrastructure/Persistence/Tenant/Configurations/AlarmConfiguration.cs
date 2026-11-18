using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class AlarmConfiguration : IEntityTypeConfiguration<Alarm>
{
    public void Configure(EntityTypeBuilder<Alarm> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("alarms");
        builder.HasKey(a => a.Id);
        builder.Ignore(a => a.DomainEvents);
        builder.Ignore(a => a.IsOpen);
        builder.Property(a => a.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.Comment).HasMaxLength(Alarm.MaxCommentLength);
        builder.HasOne<Door>().WithMany().HasForeignKey(a => a.DoorId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(a => new { a.DoorId, a.Kind }).IsUnique().HasFilter("acknowledged_at IS NULL");
        builder.HasIndex(a => a.RaisedAt);
    }
}
