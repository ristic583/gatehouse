using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Fulfillment.Events;

public sealed record FulfillmentFailed(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    FulfillmentKind Kind,
    string ErrorCode,
    string ErrorMessage,
    int Attempts,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
