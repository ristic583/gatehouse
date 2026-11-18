using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentDesired(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    PacsInstanceId PacsInstanceId,
    FulfillmentKind Kind,
    CommandPriority Priority,
    int Revision,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
