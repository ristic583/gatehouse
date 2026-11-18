namespace Gatehouse.Application.Reconciliation.Dtos;

public sealed record ReconciliationInstanceDto(
    Guid PacsInstanceId,
    string Name,
    string Vendor,
    string FulfillmentMode,
    IReadOnlyList<string> SiteNames,
    bool CanRun,
    string? BlockedReason,
    ReconciliationReportSummaryDto? LastReport);
