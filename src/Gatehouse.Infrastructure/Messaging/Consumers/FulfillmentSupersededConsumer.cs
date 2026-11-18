using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors;
using Gatehouse.Domain.Fulfillment.Events;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class FulfillmentSupersededConsumer(
    TenantDbContext db, IMessageDeduplicator deduplicator, SupersedeFulfillmentCommandsHandler handler)
    : IConsumer<FulfillmentSuperseded>
{
    public Task Consume(ConsumeContext<FulfillmentSuperseded> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        return ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? message.EventId,
            nameof(FulfillmentSupersededConsumer),
            () => handler.HandleAsync(new SupersedeFulfillmentCommandsCommand(message.FulfillmentId), context.CancellationToken),
            context.CancellationToken);
    }
}
