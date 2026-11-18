namespace Gatehouse.Application.Reconciliation.ListReconciliationReports;

public sealed record ListReconciliationReportsQuery(Guid? PacsInstanceId, int? Limit);
