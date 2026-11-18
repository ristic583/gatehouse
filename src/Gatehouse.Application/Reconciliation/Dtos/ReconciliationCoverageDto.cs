namespace Gatehouse.Application.Reconciliation.Dtos;

public sealed record ReconciliationCoverageDto(
    IReadOnlyList<ReconciliationNamedRefDto> Sites,
    IReadOnlyList<ReconciliationNamedRefDto> Areas,
    IReadOnlyList<string> RecordTypes,
    int ExportedRecordCount,
    int ComparedRecordCount,
    int InSyncCount,
    int SkippedInFlightCount,
    int IgnoredInactiveCount);
