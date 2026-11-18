using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.ListAlarms;

/// <summary>Alarms on sites the caller may view live (tenant admin, or security staff within scope), newest first.</summary>
public sealed class ListAlarmsHandler(ITenantDb db, ICurrentUser user)
{
    public const int MaxItems = 100;

    public async Task<Result<IReadOnlyList<AlarmDto>>> HandleAsync(ListAlarmsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        bool acknowledged;
        switch (query.Status?.ToUpperInvariant())
        {
            case null or "OPEN":
                acknowledged = false;
                break;
            case "ACKNOWLEDGED":
                acknowledged = true;
                break;
            default:
                return Error.Validation("alarm.invalid_status", "Status must be 'open' or 'acknowledged'.");
        }

        var alarms = acknowledged
            ? db.Alarms.AsNoTracking().Where(a => a.AcknowledgedAt != null)
            : db.Alarms.AsNoTracking().Where(a => a.AcknowledgedAt == null);
        var rows = AlarmProjection.Rows(db, alarms);
        if (!user.IsTenantAdmin && user.SecurityStaffSiteIds is { } scoped)
        {
            var sites = scoped.ToList();
            rows = rows.Where(r => sites.Contains(r.SiteId));
        }

        if (query.SiteId is { } siteId)
        {
            var site = new SiteId(siteId);
            rows = rows.Where(r => r.SiteId == site);
        }

        var items = await rows.OrderByDescending(r => r.RaisedAt).Take(MaxItems).ToListAsync(ct);
        return items.Select(r => r.ToDto()).ToList();
    }
}
