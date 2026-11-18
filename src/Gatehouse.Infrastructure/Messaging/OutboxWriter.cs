using System.Text.Json;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;

namespace Gatehouse.Infrastructure.Messaging;

public sealed class OutboxWriter(TenantDbContext db, TimeProvider time) : IOutboxWriter
{
    public void Enqueue(object message, CommandPriority priority)
    {
        ArgumentNullException.ThrowIfNull(message);
        var now = time.GetUtcNow();
        db.Set<OutboxMessage>().Add(new OutboxMessage
        {
            Id = Guid.CreateVersion7(),
            OccurredAt = now,
            Type = message.GetType().Name,
            Payload = JsonSerializer.Serialize(message, message.GetType(), OutboxJson.Options),
            Priority = priority,
            NextAttemptAt = now,
            TraceParent = Gatehouse.Application.Common.Diagnostics.GatehouseTelemetry.CurrentTraceParent(),
        });
    }
}
