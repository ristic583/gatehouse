using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

/// <summary>Final acks of <c>ActivateCard</c> commands move the card (M8); other card commands are tracked by their row.</summary>
public sealed class CardCommandAckConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, ApplyCardCommandAckHandler handler)
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
            nameof(CardCommandAckConsumer),
            () => handler.HandleAsync(message, context.CancellationToken),
            context.CancellationToken);
    }
}
