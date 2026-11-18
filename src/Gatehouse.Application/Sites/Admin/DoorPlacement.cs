using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>Door and reader geometry and references: positions inside the plan, <c>PacsDoorRef</c> unique per PACS instance.</summary>
public static class DoorPlacement
{
    public static Point Position(PointDto? position) => new(position?.X ?? -1, position?.Y ?? -1);

    public static bool Inside(Floor floor, Point p)
    {
        ArgumentNullException.ThrowIfNull(floor);
        return !double.IsNaN(p.X) && !double.IsNaN(p.Y) && p.X >= 0 && p.Y >= 0 && p.X <= floor.PlanWidth && p.Y <= floor.PlanHeight;
    }

    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static async Task<Floor> FloorOfAreaAsync(ITenantDb db, AreaId areaId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var floorId = await db.Areas.AsNoTracking().Where(a => a.Id == areaId).Select(a => a.FloorId).SingleAsync(ct);
        return await db.Floors.AsNoTracking().SingleAsync(f => f.Id == floorId, ct);
    }

    /// <summary>
    /// Same reference on any door of a site that shares a PACS instance with <paramref name="siteId"/> (or the same site when
    /// none covers it).
    /// </summary>
    public static async Task<bool> RefTakenAsync(ITenantDb db, SiteId siteId, string pacsRef, DoorId? except, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(db);
        var instances = await db.PacsInstances.AsNoTracking().ToListAsync(ct);
        var sites = instances.Where(i => i.SiteIds.Contains(siteId)).SelectMany(i => i.SiteIds).Append(siteId).Distinct().ToList();
        return await (from d in db.Doors
                      join a in db.Areas on d.AreaId equals a.Id
                      join f in db.Floors on a.FloorId equals f.Id
                      where d.PacsDoorRef == pacsRef && sites.Contains(f.SiteId) && d.Id != except
                      select d.Id).AnyAsync(ct);
    }
}
