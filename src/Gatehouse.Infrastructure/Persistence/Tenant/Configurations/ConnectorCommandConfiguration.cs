using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ConnectorCommandConfiguration : IEntityTypeConfiguration<ConnectorCommand>
{
    public void Configure(EntityTypeBuilder<ConnectorCommand> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("connector_commands");
        builder.HasKey(c => c.Id);
        builder.Ignore(c => c.DomainEvents);
        builder.Property(c => c.Type).HasMaxLength(50);
        builder.Property(c => c.PartitionKey).HasMaxLength(100);
        builder.Property(c => c.Priority).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.IdempotencyKey).HasMaxLength(200);
        builder.Property(c => c.PayloadJson).HasColumnName("payload").HasColumnType("jsonb");
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.LastError).HasMaxLength(2000);
        builder.Property(c => c.PacsRef).HasMaxLength(200);
        builder.Property(c => c.Version).IsRowVersion();
        builder.HasOne<PacsInstance>().WithMany().HasForeignKey(c => c.PacsInstanceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.IdempotencyKey).IsUnique();
        builder.HasIndex(c => new { c.PacsInstanceId, c.PartitionKey, c.Sequence }).IsUnique();
        builder.HasIndex(c => new { c.PacsInstanceId, c.Status, c.NextAttemptAt });
        builder.HasIndex(c => c.FulfillmentId);
    }
}
