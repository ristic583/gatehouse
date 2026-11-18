using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tasks;

namespace Gatehouse.Domain.Reconciliation.Events;

public sealed record ReconciliationFindingCreated(
    ReconciliationFindingId FindingId,
    ReconciliationReportId ReportId,
    FindingKind Kind,
    StaffTaskId TaskId,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
