namespace Gatehouse.Domain.Reconciliation;

/// <summary>What a completed reconciliation covered (domain-model.md §2.7), stored as jsonb.</summary>
public sealed record ReconciliationCoverage(
    IReadOnlyList<Guid> SiteIds,
    IReadOnlyList<Guid> AreaIds,
    IReadOnlyList<string> VendorAccessLevelRefs,
    IReadOnlyList<string> RecordTypes,
    int ExportedRecordCount,
    int ComparedRecordCount,
    int InSyncCount,
    int SkippedInFlightCount,
    int IgnoredInactiveCount);
