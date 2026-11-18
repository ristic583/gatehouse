using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Application.Reconciliation.ListReconciliationReports;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.GetReconciliationReport;

/// <summary>One report with coverage and findings; invisible or unknown → 404. Card numbers only appear masked.</summary>
public sealed class GetReconciliationReportHandler(ITenantDb db, ReconciliationReadModel read)
{
    public async Task<Result<ReconciliationReportDto>> HandleAsync(GetReconciliationReportQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var reportId = new ReconciliationReportId(query.ReportId);
        var report = await db.ReconciliationReports.AsNoTracking().FirstOrDefaultAsync(r => r.Id == reportId, ct);
        var instance = report is null
            ? null
            : await db.PacsInstances.AsNoTracking().FirstOrDefaultAsync(p => p.Id == report.PacsInstanceId, ct);
        if (report is null || instance is null || !await read.CanViewAsync(instance))
        {
            return Error.NotFound("reconciliation.report_not_found", "Reconciliation report not found.");
        }

        var summary = (
            await read.SummariesAsync(
                [report], new Dictionary<Domain.Fulfillment.PacsInstanceId, string> { [instance.Id] = instance.Name }, ct))[0];
        var findings = await FindingsAsync(reportId, ct);
        return new ReconciliationReportDto(
            summary, await CoverageAsync(report.Coverage, ct), report.FailureCode, report.FailureMessage, findings);
    }

    private async Task<ReconciliationCoverageDto?> CoverageAsync(ReconciliationCoverage? coverage, CancellationToken ct)
    {
        if (coverage is null)
        {
            return null;
        }

        var siteIds = coverage.SiteIds.Select(id => new Domain.Sites.SiteId(id)).ToList();
        var areaIds = coverage.AreaIds.Select(id => new Domain.Sites.AreaId(id)).ToList();
        var sites = await db.Sites.AsNoTracking().Where(s => siteIds.Contains(s.Id))
            .Select(s => new ReconciliationNamedRefDto(s.Id.Value, s.Name)).ToListAsync(ct);
        var areas = await db.Areas.AsNoTracking().Where(a => areaIds.Contains(a.Id))
            .Select(a => new ReconciliationNamedRefDto(a.Id.Value, a.Name)).ToListAsync(ct);
        return new ReconciliationCoverageDto(
            [.. sites.OrderBy(s => s.Name, StringComparer.Ordinal)],
            [.. areas.OrderBy(a => a.Name, StringComparer.Ordinal)],
            coverage.RecordTypes,
            coverage.ExportedRecordCount,
            coverage.ComparedRecordCount,
            coverage.InSyncCount,
            coverage.SkippedInFlightCount,
            coverage.IgnoredInactiveCount);
    }

    private async Task<List<ReconciliationFindingDto>> FindingsAsync(ReconciliationReportId reportId, CancellationToken ct)
    {
        var findings = await db.ReconciliationFindings.AsNoTracking().Where(f => f.ReportId == reportId).ToListAsync(ct);
        var personIds = findings.Where(f => f.PersonId is not null).Select(f => f.PersonId!.Value).Distinct().ToList();
        var areaIds = findings.Where(f => f.AreaId is not null).Select(f => f.AreaId!.Value).Distinct().ToList();
        var taskIds = findings.Where(f => f.StaffTaskId is not null).Select(f => f.StaffTaskId!.Value).Distinct().ToList();
        var persons = await db.Persons.AsNoTracking().Where(p => personIds.Contains(p.Id))
            .Select(p => new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName)).ToDictionaryAsync(p => p.PersonId, ct);
        var areas = await db.Areas.AsNoTracking().Where(a => areaIds.Contains(a.Id)).ToDictionaryAsync(a => a.Id, a => a.Name, ct);
        var tasks = await db.StaffTasks.AsNoTracking().Where(t => taskIds.Contains(t.Id))
            .ToDictionaryAsync(t => t.Id, t => new { t.Status, t.ResolutionAction }, ct);

        return [.. findings
            .OrderBy(f => f.Kind)
            .ThenBy(f => f.PersonRef, StringComparer.Ordinal)
            .ThenBy(f => f.VendorAccessLevelRef, StringComparer.Ordinal)
            .ThenBy(f => f.PacsRef, StringComparer.Ordinal)
            .Select(f =>
            {
                var task = f.StaffTaskId is { } tid ? tasks.GetValueOrDefault(tid) : null;
                return new ReconciliationFindingDto(
                    f.Id.Value,
                    f.Kind.ToString(),
                    f.PersonId is { } pid ? persons.GetValueOrDefault(pid.Value) : null,
                    f.PersonRef,
                    f.AreaId?.Value,
                    f.AreaId is { } aid ? areas.GetValueOrDefault(aid) : null,
                    f.VendorAccessLevelRef,
                    f.PacsRef,
                    f.Details.Differences,
                    f.Details.Expected,
                    f.Details.Actual,
                    f.StaffTaskId?.Value ?? Guid.Empty,
                    task?.Status.ToString() ?? string.Empty,
                    task?.ResolutionAction?.ToString());
            })];
    }
}
