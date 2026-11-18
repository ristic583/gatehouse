using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentDriftDetected(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    Guid? FindingId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
