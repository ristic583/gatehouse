namespace Gatehouse.Domain.Reconciliation;

public readonly record struct ReconciliationReportId(Guid Value)
{
    public static ReconciliationReportId New() => new(Guid.CreateVersion7());
}
