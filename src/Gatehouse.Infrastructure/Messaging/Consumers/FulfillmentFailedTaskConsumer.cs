using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Tasks.FulfillmentFollowUps;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class FulfillmentFailedTaskConsumer(
    TenantDbContext db, IMessageDeduplicator deduplicator, OpenFulfillmentFailedTaskHandler handler)
    : IConsumer<FulfillmentFailed>
{
    public Task Consume(ConsumeContext<FulfillmentFailed> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? message.EventId,
            nameof(FulfillmentFailedTaskConsumer),
            () =>
                handler.HandleAsync(
                    new OpenFulfillmentFailedTaskCommand(message.FulfillmentId, message.OccurredAt), context.CancellationToken),
            context.CancellationToken);
    }
}
