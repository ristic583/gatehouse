using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors.ReceiveDoorEvents;

/// <summary>
/// <c>POST /connector/v1/events</c> (connector-protocol.md §2.4): dedupes by event id, hashes card numbers (keyed, per tenant) and
/// publishes <see cref="DoorEventsReceived"/> through the tenant outbox.
/// </summary>
public sealed class ReceiveDoorEventsHandler(
    ITenantDb db,
    ConnectorContext connector,
    IOutboxWriter outbox,
    IDoorEventDeduplicator deduplicator,
    ICardNumberHasher cardHasher,
    TimeProvider time)
{
    public const int MaxEvents = 500;

    public async Task<Result<DoorEventsResponse>> HandleAsync(DoorEventsRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Events is null || request.Events.Count == 0)
        {
            return Error.Validation("connector.validation_failed", "At least one event is required.");
        }

        if (request.Events.Count > MaxEvents)
        {
            return Error.Validation("connector.payload_too_large", $"At most {MaxEvents} events per request.");
        }

        var accepted = new List<NormalizedDoorEvent>(request.Events.Count);
        foreach (var e in request.Events.DistinctBy(e => e.EventId))
        {
            if (await deduplicator.TryClaimAsync(connector.TenantId, e.EventId, ct))
            {
                accepted.Add(new NormalizedDoorEvent(
                    e.EventId, e.OccurredAt, e.Type, e.PacsDoorRef, e.PacsReaderRef, cardHasher.Hash(connector.TenantId, e.CardNumber),
                    e.PersonRef, e.Direction));
            }
        }

        if (accepted.Count > 0)
        {
            try
            {
                outbox.Enqueue(
                    new DoorEventsReceived(
                        connector.TenantId.Value, connector.PacsInstanceId, connector.ConnectorId, time.GetUtcNow(), accepted),
                    CommandPriority.Normal);
                await db.SaveChangesAsync(ct);
            }
            catch
            {
                await deduplicator.ReleaseAsync(connector.TenantId, accepted.Select(e => e.EventId).ToList(), CancellationToken.None);
                throw;
            }
        }

        return new DoorEventsResponse(accepted.Count, request.Events.Count - accepted.Count);
    }
}
