using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record RevocationUnconfirmedAlert(
    EntitlementId EntitlementId,
    FulfillmentId FulfillmentId,
    DateTimeOffset RevokedAt,
    TimeSpan Outstanding,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
