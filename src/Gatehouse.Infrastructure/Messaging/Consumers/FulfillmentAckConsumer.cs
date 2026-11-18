using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class FulfillmentAckConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, ApplyConnectorAckHandler handler)
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
            nameof(FulfillmentAckConsumer),
            () => handler.HandleAsync(message, context.CancellationToken),
            context.CancellationToken);
    }
}
