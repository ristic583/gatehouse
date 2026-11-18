using Gatehouse.Application.Access.Notifications;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Domain.Access.Events;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary>Emails the approver of a newly activated approval step (deduplicated per message).</summary>
public sealed class ApprovalNotificationConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, NotifyApproversHandler handler)
    : IConsumer<ApprovalStepActivated>
{
    public Task Consume(ConsumeContext<ApprovalStepActivated> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? message.EventId,
            nameof(ApprovalNotificationConsumer),
            async () =>
            {
                await handler.HandleAsync(
                    new NotifyApproversCommand(message.EntitlementId, message.StepId, message.AssignedApproverId),
                    context.CancellationToken);
            },
            context.CancellationToken);
    }
}
