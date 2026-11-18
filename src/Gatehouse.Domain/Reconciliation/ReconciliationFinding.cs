using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Domain.Reconciliation;

/// <summary>A difference between Gatehouse and a PACS export (domain-model.md §2.7). <see cref="PacsCardNumber"/> is the full number and
/// never leaves the database.</summary>
public sealed class ReconciliationFinding : Entity<ReconciliationFindingId>
{
    private ReconciliationFinding(ReconciliationFindingId id)
        : base(id)
    {
    }

    // EF Core
    private ReconciliationFinding()
        : base(default)
    {
    }

    public ReconciliationReportId ReportId { get; private set; }

    public FindingKind Kind { get; private set; }

    public PersonId? PersonId { get; private set; }

    public AreaId? AreaId { get; private set; }

    public EntitlementId? EntitlementId { get; private set; }

    public FulfillmentId? FulfillmentId { get; private set; }

    public string? PacsRef { get; private set; }

    public string? PersonRef { get; private set; }

    public string VendorAccessLevelRef { get; private set; } = string.Empty;

    [Sensitive]
    public string? PacsCardNumber { get; private set; }

    public FindingDetails Details { get; private set; } = new([], null, null);

    public StaffTaskId? StaffTaskId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public static ReconciliationFinding Create(
        ReconciliationReportId reportId,
        FindingKind kind,
        PersonId? personId,
        AreaId? areaId,
        EntitlementId? entitlementId,
        FulfillmentId? fulfillmentId,
        string? pacsRef,
        string? personRef,
        string vendorAccessLevelRef,
        string? pacsCardNumber,
        FindingDetails details,
        DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vendorAccessLevelRef);
        ArgumentNullException.ThrowIfNull(details);
        return new ReconciliationFinding(ReconciliationFindingId.New())
        {
            ReportId = reportId,
            Kind = kind,
            PersonId = personId,
            AreaId = areaId,
            EntitlementId = entitlementId,
            FulfillmentId = fulfillmentId,
            PacsRef = pacsRef,
            PersonRef = personRef,
            VendorAccessLevelRef = vendorAccessLevelRef,
            PacsCardNumber = pacsCardNumber,
            Details = details,
            CreatedAt = now,
        };
    }

    /// <summary>Links the staff task that handles this finding; set once.</summary>
    public void AttachTask(StaffTaskId taskId)
    {
        if (StaffTaskId is not null)
        {
            throw new InvalidOperationException("The finding already has a staff task.");
        }

        StaffTaskId = taskId;
    }
}
