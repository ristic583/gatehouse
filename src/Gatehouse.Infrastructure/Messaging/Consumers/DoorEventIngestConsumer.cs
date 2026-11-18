using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.LiveBuilding.IngestDoorEvents;
using Gatehouse.Infrastructure.Persistence.Tenant;
using MassTransit;

namespace Gatehouse.Infrastructure.Messaging.Consumers;

public sealed class DoorEventIngestConsumer(TenantDbContext db, IMessageDeduplicator deduplicator, IngestDoorEventsHandler handler)
    : IConsumer<DoorEventsReceived>
{
    public async Task Consume(ConsumeContext<DoorEventsReceived> context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var message = context.Message;
        var ct = context.CancellationToken;
        IngestDoorEventsOutcome? outcome = null;
        var committed = await ConsumerTransaction.RunAsync(
            db,
            deduplicator,
            context.MessageId ?? Guid.CreateVersion7(),
            nameof(DoorEventIngestConsumer),
            async () =>
            {
                var result = await handler.HandleAsync(
                    new IngestDoorEventsCommand(message.PacsInstanceId, message.ReceivedAt, message.Events), ct);
                outcome = result.IsSuccess ? result.Value : null;
            },
            ct);
        // Only a committed transaction is pushed to the cache and live feed (a discarded duplicate never is: GH-CONN-04).
        if (committed && outcome is not null)
        {
            await handler.PublishAsync(outcome, ct);
        }
    }
}
