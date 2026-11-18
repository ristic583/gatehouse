namespace Gatehouse.Application.Common.Messaging;

/// <summary>Consumer idempotency (ADR-005): records the message in the tenant context; the caller saves in the same SaveChanges as its
/// work.</summary>
public interface IMessageDeduplicator
{
    /// <summary>False when the message was already processed by this consumer.</summary>
    public Task<bool> TryMarkProcessedAsync(Guid messageId, string consumer, CancellationToken ct);
}
