using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentDispatched(
    FulfillmentId FulfillmentId,
    CommandId CommandId,
    int Attempt,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
