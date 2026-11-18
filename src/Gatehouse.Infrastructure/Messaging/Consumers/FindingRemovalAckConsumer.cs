using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Reconciliation.Remediation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary>A final ack of a reconciliation <c>RemovedFromPacs</c> revoke resolves (or keeps open) the finding's task
/// (GH-LIVE-02).</summary>
public sealed class FindingRemovalAckConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, ApplyFindingRemovalAckHandler handler)
    : IConsumer<ConnectorCommandAcked>
{
    public Task Consume(ConsumeContext<ConnectorCommandAcked> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? Guid.CreateVersion7(),
            nameof(FindingRemovalAckConsumer),
            () => handler.HandleAsync(message, context.CancellationToken),
            context.CancellationToken);
    }
}
