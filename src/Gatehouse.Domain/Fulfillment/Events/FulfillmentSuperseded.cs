using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentSuperseded(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    FulfillmentKind Kind,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
