using Gatehouse.Domain.LiveBuilding;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

/// <summary>
/// Range-partitioned by day (ADR-006). The table DDL is raw SQL in migration M5_LiveBuilding, so EF excludes it from migrations.
/// </summary>
public sealed class DoorEventConfiguration : IEntityTypeConfiguration<DoorEvent>
{
    public void Configure(EntityTypeBuilder<DoorEvent> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("door_events", t => t.ExcludeFromMigrations());
        builder.HasKey(e => new { e.OccurredAt, e.Id });
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(e => e.Direction).HasConversion<string>().HasMaxLength(10);
        builder.Property(e => e.PacsDoorRef).HasMaxLength(100);
        builder.Property(e => e.PacsReaderRef).HasMaxLength(100);
        builder.Property(e => e.CardNumberHash).HasMaxLength(64);
    }
}
