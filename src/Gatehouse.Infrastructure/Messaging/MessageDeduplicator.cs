using Gatehouse.Application.Common.Messaging;
using Gatehouse.Infrastructure.Messaging.Outbox;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Infrastructure.Messaging;

public sealed class MessageDeduplicator(TenantDbContext db, TimeProvider time) : IMessageDeduplicator
{
    public async Task<bool> TryMarkProcessedAsync(Guid messageId, string consumer, CancellationToken ct)
    {
        if (await db.ProcessedMessages.AnyAsync(m => m.MessageId == messageId && m.Consumer == consumer, ct))
        {
            return false;
        }

        db.ProcessedMessages.Add(new ProcessedMessage { MessageId = messageId, Consumer = consumer, ProcessedAt = time.GetUtcNow() });
        return true;
    }
}
