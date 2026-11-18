using Gatehouse.Domain.LiveBuilding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class DoorCommandConfiguration : IEntityTypeConfiguration<DoorCommand>
{
    public void Configure(EntityTypeBuilder<DoorCommand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("door_commands");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DomainEvents);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Scope).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Reason).HasMaxLength(DoorCommand.MaxReasonLength);
        builder.HasIndex(c => c.RequestedAt);
    }
}
