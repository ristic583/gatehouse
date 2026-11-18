using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Reconciliation.CompareReconciliation;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary><c>RecordExportReceived</c> → compare the staged export (<c>exportId == reportId</c>).</summary>
public sealed class ReconciliationExportConsumer(
    TenantDbContext db, IMessageDeduplicator deduplicator, CompareReconciliationHandler handler)
    : IConsumer<RecordExportReceived>
{
    public Task Consume(ConsumeContext<RecordExportReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? Guid.CreateVersion7(),
            nameof(ReconciliationExportConsumer),
            () => handler.HandleAsync(new ReconciliationReportId(message.ExportId), context.CancellationToken),
            context.CancellationToken);
    }
}
