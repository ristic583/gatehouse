namespace Gatehouse.Domain.Reconciliation;

public readonly record struct ReconciliationFindingId(Guid Value)
{
    public static ReconciliationFindingId New() => new(Guid.CreateVersion7());
}
