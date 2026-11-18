using Gatehouse.Application.Common.Messaging;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Gatehouse.Infrastructure.Messaging;

/// <summary>
/// Pattern for every consumer: mark the message processed, do the work, save once. A unique violation on
/// <c>processed_messages</c> means a concurrent delivery won; the work is discarded and the consumer returns normally. Any other
/// unique violation (a business constraint) is rethrown so the message is retried (GH-CONN-04). Returns whether this delivery's work
/// committed, so callers publish after-commit effects only then.
/// </summary>
public static class ConsumerTransaction
{
    public const string ProcessedMessagesKey = "pk_processed_messages";

    public static async Task<bool> RunAsync(
        TenantDbContext db,
        IMessageDeduplicator deduplicator,
        Guid messageId,
        string consumer,
        Func<Task> work,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(deduplicator);
        ArgumentNullException.ThrowIfNull(work);
        if (!await deduplicator.TryMarkProcessedAsync(messageId, consumer, ct))
        {
            return false;
        }

        await work();
        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: ProcessedMessagesKey,
        })
        {
            // Duplicate delivery processed concurrently.
            return false;
        }
    }
}
