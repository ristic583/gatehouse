using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.Dtos;
using Gatehouse.Application.Reconciliation.ListReconciliationReports;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.ListReconciliationInstances;

/// <summary>
/// PACS instances the caller may view, with whether they can start a reconciliation now. <c>BlockedReason</c> is the 409 code
/// a request would get; <c>CanRun</c> also needs <c>RunReconciliationRequirement</c> (never the tenant admin).
/// </summary>
public sealed class ListReconciliationInstancesHandler(ITenantDb db, ReconciliationReadModel read)
{
    public async Task<Result<IReadOnlyList<ReconciliationInstanceDto>>> HandleAsync(CancellationToken ct)
    {
        var instances = await read.VisibleInstancesAsync(ct);
        var siteIds = instances.SelectMany(i => i.SiteIds).Distinct().ToList();
        var siteNames = await db.Sites.AsNoTracking().Where(s => siteIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var names = instances.ToDictionary(i => i.Id, i => i.Name);

        var result = new List<ReconciliationInstanceDto>();
        foreach (var instance in instances)
        {
            var last = await db.ReconciliationReports.AsNoTracking()
                .Where(r => r.PacsInstanceId == instance.Id)
                .OrderByDescending(r => r.RequestedAt)
                .FirstOrDefaultAsync(ct);
            var blocked = BlockedReason(instance, last);
            var canRun = blocked is null && await read.CanRunAsync(instance);
            var summary = last is null ? null : (await read.SummariesAsync([last], names, ct))[0];
            result.Add(new ReconciliationInstanceDto(
                instance.Id.Value,
                instance.Name,
                instance.Vendor.ToString(),
                instance.FulfillmentMode.ToString(),
                [.. instance.SiteIds.Select(s => siteNames.GetValueOrDefault(s, string.Empty)).Order(StringComparer.Ordinal)],
                canRun,
                blocked,
                summary));
        }

        return result;
    }

    private static string? BlockedReason(PacsInstance instance, ReconciliationReport? last)
    {
        if (instance.FulfillmentMode != FulfillmentMode.Connector)
        {
            return "reconciliation.connector_required";
        }

        if (PacsCapabilityGaps.From(instance.CapabilitiesJson) is { } gaps && gaps.Contains(PacsCapabilityGaps.RecordExport))
        {
            return "reconciliation.export_unsupported";
        }

        return last is { IsRunning: true } ? "reconciliation.already_running" : null;
    }
}
