using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Infrastructure.Persistence.Tenant.Conventions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class AreaConfiguration : IEntityTypeConfiguration<Area>
{
    public void Configure(EntityTypeBuilder<Area> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("areas");
        builder.HasKey(a => a.Id);
        builder.Ignore(a => a.DomainEvents);
        builder.Property(a => a.Name).HasMaxLength(100);
        builder.Property(a => a.Code).HasMaxLength(30);
        builder.Property(a => a.Polygon)
            .HasColumnType("jsonb")
            .HasConversion(JsonValueConversions.PolygonConverter, JsonValueConversions.PolygonComparer);
        builder.Property(a => a.AccessControlled).HasDefaultValue(true).HasSentinel(true);
        builder.Property(a => a.Version).IsRowVersion();
        builder.HasOne<Floor>().WithMany().HasForeignKey(a => a.FloorId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SecurityLevel>().WithMany().HasForeignKey(a => a.SecurityLevelId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.FloorId, a.Name }).IsUnique();
        builder.HasIndex(a => a.Code).IsUnique();

        builder.OwnsMany(a => a.Owners, owner =>
        {
            owner.ToTable("area_owners");
            owner.WithOwner().HasForeignKey("AreaId");
            owner.Property(o => o.PersonId);
            owner.HasKey("AreaId", nameof(AreaOwner.PersonId));
            owner.HasOne<Person>().WithMany().HasForeignKey(o => o.PersonId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(a => a.Owners).HasField("_owners").UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
