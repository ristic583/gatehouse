namespace Gatehouse.Domain.Reconciliation;

public sealed record ReconciliationOutcome(
    IReadOnlyList<FindingDraft> Findings,
    int ComparedRecordCount,
    int InSyncCount,
    int SkippedInFlightCount,
    int IgnoredInactiveCount);
