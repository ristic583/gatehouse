using System.Security.Claims;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.ListReconciliationReports;

/// <summary>Shared reads for the reconciliation endpoints: instance visibility (authorization.md §10) and report summaries.</summary>
public sealed class ReconciliationReadModel(ITenantDb db, IAuthorizationService authorization)
{
    public async Task<List<PacsInstance>> VisibleInstancesAsync(CancellationToken ct)
    {
        var instances = await db.PacsInstances.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        var visible = new List<PacsInstance>();
        foreach (var instance in instances)
        {
            if (await CanViewAsync(instance))
            {
                visible.Add(instance);
            }
        }

        return visible;
    }

    public async Task<bool> CanViewAsync(PacsInstance instance) =>
        (
            await authorization.AuthorizeAsync(
                new ClaimsPrincipal(), ReconciliationScope.ResourceFor(instance), new ViewReconciliationRequirement())).Succeeded;

    public async Task<bool> CanRunAsync(PacsInstance instance) =>
        (
            await authorization.AuthorizeAsync(
                new ClaimsPrincipal(), ReconciliationScope.ResourceFor(instance), new RunReconciliationRequirement())).Succeeded;

    public async Task<List<ReconciliationReportSummaryDto>> SummariesAsync(
        IReadOnlyList<ReconciliationReport> reports,
        IReadOnlyDictionary<PacsInstanceId, string> instanceNames,
        CancellationToken ct)
    {
        var ids = reports.Select(r => r.Id).ToList();
        var findingCounts = await db.ReconciliationFindings.AsNoTracking()
            .Where(f => ids.Contains(f.ReportId))
            .GroupBy(f => f.ReportId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var requesterIds = reports.Where(r => r.RequestedById is not null).Select(r => r.RequestedById!.Value).Distinct().ToList();
        var requesters = await db.Persons.AsNoTracking()
            .Where(p => requesterIds.Contains(p.Id))
            .Select(p => new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName))
            .ToDictionaryAsync(p => p.PersonId, ct);

        return [.. reports.Select(r =>
        {
            var completed = r.Status == ReconciliationStatus.Completed;
            return new ReconciliationReportSummaryDto(
                r.Id.Value,
                r.PacsInstanceId.Value,
                instanceNames.GetValueOrDefault(r.PacsInstanceId, string.Empty),
                r.Status.ToString(),
                r.RequestedAt,
                r.RequestedById is { } by ? requesters.GetValueOrDefault(by.Value) : null,
                r.CapturedAt,
                r.ComparedAt,
                completed ? findingCounts.GetValueOrDefault(r.Id) : null,
                r.Coverage?.ComparedRecordCount,
                r.Coverage?.InSyncCount);
        })];
    }
}
