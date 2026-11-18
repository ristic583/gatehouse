using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Domain.Reconciliation.Events;

public sealed record ReconciliationRequested(
    ReconciliationReportId ReportId,
    PacsInstanceId PacsInstanceId,
    PersonId? RequestedById,
    DateTimeOffset OccurredAt) : DomainEvent(OccurredAt);
