using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Tasks.FulfillmentFollowUps;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class FulfillmentConfirmedTaskConsumer(
    TenantDbContext db, IMessageDeduplicator deduplicator, ResolveFulfillmentTasksHandler handler)
    : IConsumer<FulfillmentConfirmed>
{
    public Task Consume(ConsumeContext<FulfillmentConfirmed> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? message.EventId,
            nameof(FulfillmentConfirmedTaskConsumer),
            () =>
                handler.HandleAsync(
                    new ResolveFulfillmentTasksCommand(
                        message.FulfillmentId, message.ConfirmedByKind, message.ConfirmedById, message.OccurredAt),
                    context.CancellationToken),
            context.CancellationToken);
    }
}
