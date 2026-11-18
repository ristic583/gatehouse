using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>A finding before it is stored. <see cref="PacsCardNumber"/> is the full number (never put it in DTOs or logs).</summary>
public sealed record FindingDraft(
    FindingKind Kind,
    PersonId? PersonId,
    AreaId? AreaId,
    EntitlementId? EntitlementId,
    FulfillmentId? FulfillmentId,
    string? PacsRef,
    string? PersonRef,
    string VendorAccessLevelRef,
    string? PacsCardNumber,
    FindingDetails Details);
