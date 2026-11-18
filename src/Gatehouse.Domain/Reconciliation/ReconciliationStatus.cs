namespace Gatehouse.Domain.Reconciliation;

public enum ReconciliationStatus
{
    Requested,
    Exporting,
    Comparing,
    Completed,
    Failed,
}
