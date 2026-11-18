using Gatehouse.Application.Access.Approvals;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class FulfillmentConfiguration : IEntityTypeConfiguration<FulfillmentEntity>
{
    public void Configure(EntityTypeBuilder<FulfillmentEntity> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("fulfillments");
        builder.HasKey(f => f.Id);
        builder.Ignore(f => f.DomainEvents);
        builder.Property(f => f.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(f => f.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(f => f.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(f => f.ConfirmedByKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(f => f.ConfirmedById).HasMaxLength(100);
        builder.Property(f => f.PacsRef).HasMaxLength(200);
        builder.Property(f => f.LastError).HasMaxLength(2000);
        builder.Property(f => f.Version).IsRowVersion();
        builder.HasOne<Entitlement>().WithMany().HasForeignKey(f => f.EntitlementId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<PacsInstance>().WithMany().HasForeignKey(f => f.PacsInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(f => new { f.EntitlementId, f.PacsInstanceId, f.Kind }).IsUnique()
            .HasDatabaseName(StepDecisionService.FulfillmentUniqueIndex);
        builder.HasIndex(f => f.Status);
    }
}
