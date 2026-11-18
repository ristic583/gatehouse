using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gatehouse.Infrastructure.Messaging.Outbox;

public sealed class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.ToTable("outbox_messages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Type).HasMaxLength(200);
        builder.Property(m => m.Payload).HasColumnType("jsonb");
        builder.Property(m => m.Priority).HasConversion<string>().HasMaxLength(10);
        builder.HasIndex(m => new { m.DispatchedAt, m.Priority, m.OccurredAt });
    }
}
