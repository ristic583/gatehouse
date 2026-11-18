using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Common.Messaging;

/// <summary>Adds a message to the current tenant outbox; saved by the caller's <c>SaveChangesAsync</c> (ADR-005).</summary>
public interface IOutboxWriter
{
    public void Enqueue(object message, CommandPriority priority);
}
