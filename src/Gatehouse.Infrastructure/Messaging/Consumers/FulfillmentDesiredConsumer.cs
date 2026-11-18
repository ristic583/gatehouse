using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class FulfillmentDesiredConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, DispatchFulfillmentHandler handler)
    : IConsumer<FulfillmentDesired>
{
    public Task Consume(ConsumeContext<FulfillmentDesired> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? message.EventId,
            nameof(FulfillmentDesiredConsumer),
            () => handler.HandleAsync(new DispatchFulfillmentCommand(message.FulfillmentId), context.CancellationToken),
            context.CancellationToken);
    }
}
