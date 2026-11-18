using Gatehouse.Domain.Common;

namespace Gatehouse.Domain.Reconciliation.Events;

public sealed record ReconciliationFailed(
    ReconciliationReportId ReportId,
    string Code,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
