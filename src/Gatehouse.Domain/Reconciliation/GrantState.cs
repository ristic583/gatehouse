using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>A <c>Grant</c> fulfillment of the reconciled instance with its entitlement's state.</summary>
public sealed record GrantState(
    FulfillmentId FulfillmentId,
    EntitlementId EntitlementId,
    PersonId PersonId,
    AreaId AreaId,
    string VendorAccessLevelRef,
    FulfillmentStatus Status,
    EntitlementStatus EntitlementStatus,
    DateTimeOffset RequestedStart,
    DateTimeOffset EffectiveEnd);
