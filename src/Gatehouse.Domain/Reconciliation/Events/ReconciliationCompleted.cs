using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Reconciliation.Events;

public sealed record ReconciliationCompleted(
    ReconciliationReportId ReportId,
    int ComparedRecords,
    int FindingCount,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
