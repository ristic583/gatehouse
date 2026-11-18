using Gatehouse.Application.People.Dtos;

namespace Gatehouse.Application.Reconciliation.Dtos;

public sealed record ReconciliationReportSummaryDto(
    Guid ReportId,
    Guid PacsInstanceId,
    string PacsInstanceName,
    string Status,
    DateTimeOffset RequestedAt,
    PersonRefDto? RequestedBy,
    DateTimeOffset? CapturedAt,
    DateTimeOffset? ComparedAt,
    int? FindingCount,
    int? ComparedRecordCount,
    int? InSyncCount);
