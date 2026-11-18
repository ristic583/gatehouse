using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

public sealed class ProcessedMessageConfiguration : IEntityTypeConfiguration<ProcessedMessage>
{
    public void Configure(EntityTypeBuilder<ProcessedMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("processed_messages");
        builder.HasKey(m => new { m.MessageId, m.Consumer });
        builder.Property(m => m.Consumer).HasMaxLength(200);
    }
}
