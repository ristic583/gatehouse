using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentConfirmed(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    FulfillmentKind Kind,
    ConfirmerKind ConfirmedByKind,
    string ConfirmedById,
    DateTimeOffset ConfirmedAt,
    string? PacsRef,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
