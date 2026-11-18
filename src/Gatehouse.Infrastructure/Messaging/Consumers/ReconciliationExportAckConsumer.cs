using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Reconciliation.FailReconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary>A final non-<c>Applied</c> ack of an <c>ExportRecords</c> command fails its report.</summary>
public sealed class ReconciliationExportAckConsumer(
    TenantDbContext db, IMessageDeduplicator deduplicator, FailReconciliationHandler handler)
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
            nameof(ReconciliationExportAckConsumer),
            () => handler.HandleAckAsync(message, context.CancellationToken),
            context.CancellationToken);
    }
}
