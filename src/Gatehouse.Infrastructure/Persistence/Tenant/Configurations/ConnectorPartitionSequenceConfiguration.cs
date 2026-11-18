using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Persistence.Tenant.Configurations;

public sealed class ConnectorPartitionSequenceConfiguration : IEntityTypeConfiguration<ConnectorPartitionSequence>
{
    public void Configure(EntityTypeBuilder<ConnectorPartitionSequence> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("connector_partition_sequences");
        builder.HasKey(s => new { s.PacsInstanceId, s.PartitionKey });
        builder.Property(s => s.PartitionKey).HasMaxLength(100);
    }
}
