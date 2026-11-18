namespace Gatehouse.Application.Reconciliation.RequestReconciliation;

/// <summary>Body of <c>POST /api/v1/reconciliation/reports</c>.</summary>
public sealed record RequestReconciliationRequest(Guid PacsInstanceId);
