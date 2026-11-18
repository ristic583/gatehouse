using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class EntitlementConfiguration : IEntityTypeConfiguration<Entitlement>
{
    public void Configure(EntityTypeBuilder<Entitlement> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("entitlements");
        builder.HasKey(e => e.Id);
        builder.Ignore(e => e.DomainEvents);
        builder.Ignore(e => e.PendingStep);
        builder.Property(e => e.Justification).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Version).IsRowVersion();
        builder.HasOne<Person>().WithMany().HasForeignKey(e => e.PersonId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Person>().WithMany().HasForeignKey(e => e.RequestedById).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Area>().WithMany().HasForeignKey(e => e.AreaId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => new { e.PersonId, e.AreaId, e.Status });

        // At most one open request per person and area, enforced by the database so concurrent submissions cannot both pass the
        // duplicate check (GH-ACCESS-03). Renewals open their request only while no other request is pending.
        builder.HasIndex(e => new { e.PersonId, e.AreaId })
            .IsUnique()
            .HasFilter("status = 'PendingApproval'")
            .HasDatabaseName("ux_entitlements_pending_person_area");

        builder.OwnsMany(e => e.Steps, step =>
        {
            step.ToTable("approval_steps");
            step.WithOwner().HasForeignKey("EntitlementId");
            step.HasKey(s => s.Id);
            step.Property(s => s.Id).ValueGeneratedNever();
            step.Property(s => s.ApproverKind).HasConversion<string>().HasMaxLength(20);
            step.Property(s => s.Status).HasConversion<string>().HasMaxLength(20);
            step.Property(s => s.Comment).HasMaxLength(500);
            step.HasIndex(s => new { s.Status, s.ApproverKind });
        });
        builder.Navigation(e => e.Steps).HasField("_steps").UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsOne(e => e.Revocation, revocation =>
        {
            revocation.Property(r => r.RevokedById).HasColumnName("revocation_revoked_by_id");
            revocation.Property(r => r.RevokedAt).HasColumnName("revocation_revoked_at");
            revocation.Property(r => r.Reason).HasColumnName("revocation_reason").HasMaxLength(500);
            revocation.Property(r => r.Kind).HasColumnName("revocation_kind").HasConversion<string>().HasMaxLength(20);
        });
    }
}
