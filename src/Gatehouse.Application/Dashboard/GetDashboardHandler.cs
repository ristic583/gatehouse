using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Dashboard;

/// <summary>
/// <c>GET /api/v1/dashboard</c> (StaffConsole). Alarms and fulfillments follow the caller's site scope (tenant admins and tenant-wide
/// staff:
/// all); tasks use <c>VisibleTo</c>; approvals, reconciliation and connector health are tenant-wide counts.
/// </summary>
public sealed class GetDashboardHandler(ITenantDb db, ICurrentUser me, TimeProvider time)
{
    public async Task<DashboardDto> HandleAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        IReadOnlySet<SiteId>? scope = me.IsTenantAdmin ? null : me.SecurityStaffSiteIds;
        var scopedSiteIds = scope?.ToList();

        var pendingApprovals = await db.Entitlements.CountAsync(e => e.Status == EntitlementStatus.PendingApproval, ct);

        var alarmSites = from al in db.Alarms
                         join d in db.Doors on al.DoorId equals d.Id
                         join a in db.Areas on d.AreaId equals a.Id
                         join f in db.Floors on a.FloorId equals f.Id
                         where al.AcknowledgedAt == null
                         select f.SiteId;
        var openAlarms = scopedSiteIds is null
            ? await alarmSites.CountAsync(ct)
            : await alarmSites.CountAsync(s => scopedSiteIds.Contains(s), ct);

        var tasks = await db.StaffTasks.AsNoTracking().VisibleTo(me).Where(t => t.Status != StaffTaskStatus.Resolved)
            .GroupBy(t => t.Kind).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        var byKind = tasks.ToDictionary(t => t.Key.ToString(), t => t.Count, StringComparer.Ordinal);

        var cutoff = now.AddHours(-1);
        var staleInstanceIds = await db.Fulfillments.AsNoTracking()
            .Where(fu => (fu.Status == FulfillmentStatus.Desired || fu.Status == FulfillmentStatus.Pending) && fu.CreatedAt < cutoff)
            .Select(fu => fu.PacsInstanceId)
            .ToListAsync(ct);
        var instanceSites = (await db.PacsInstances.AsNoTracking().ToListAsync(ct)).ToDictionary(p => p.Id, p => p.SiteIds);
        var unconfirmed = scope is null
            ? staleInstanceIds.Count
            : staleInstanceIds.Count(id => instanceSites.TryGetValue(id, out var sites) && sites.Any(scope.Contains));

        var instances = await db.PacsInstances.AsNoTracking()
            .Where(p => p.FulfillmentMode == FulfillmentMode.Connector)
            .OrderBy(p => p.Name)
            .ToListAsync(ct);
        var instanceIds = instances.Select(i => i.Id).ToList();
        var reports = await db.ReconciliationReports.AsNoTracking()
            .Where(r => instanceIds.Contains(r.PacsInstanceId) && r.Status == ReconciliationStatus.Completed)
            .ToListAsync(ct);
        var reportIds = reports.Select(r => r.Id).ToList();
        var findingCounts = await db.ReconciliationFindings.AsNoTracking().Where(f => reportIds.Contains(f.ReportId))
            .GroupBy(f => f.ReportId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.Count, ct);
        var coverage = instances.Select(i =>
        {
            var last = reports.Where(r => r.PacsInstanceId == i.Id).OrderByDescending(r => r.ComparedAt).FirstOrDefault();
            return new DashboardCoverageDto(
                i.Id.Value, i.Name, last?.ComparedAt, last?.Coverage?.ComparedRecordCount, last?.Coverage?.InSyncCount,
                last is null ? null : findingCounts.GetValueOrDefault(last.Id));
        }).ToList();

        var connectors = new DashboardConnectorsDto(
            instances.Count(i => i.LastHeartbeatAt is not null && i.IsOnline(now)),
            instances.Count(i => i.LastHeartbeatAt is not null && !i.IsOnline(now)),
            instances.Count(i => i.LastHeartbeatAt is null));

        return new DashboardDto(now, pendingApprovals, openAlarms, byKind,
            byKind.GetValueOrDefault(nameof(StaffTaskKind.ReconciliationFinding)), byKind
                .GetValueOrDefault(nameof(StaffTaskKind.DriftAlert)),
            unconfirmed, coverage, connectors);
    }
}
