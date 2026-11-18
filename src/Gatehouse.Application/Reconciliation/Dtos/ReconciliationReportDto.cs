namespace Gatehouse.Application.Reconciliation.Dtos;

public sealed record ReconciliationReportDto(
    ReconciliationReportSummaryDto Summary,
    ReconciliationCoverageDto? Coverage,
    string? FailureCode,
    string? FailureMessage,
    IReadOnlyList<ReconciliationFindingDto> Findings);
