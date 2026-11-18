using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding;

/// <summary>
/// Which connector-mode PACS instance handles each door: the instance the door's area is mapped to (when that instance
/// is in Connector mode), else the site's connector instance. Doors without <c>PacsDoorRef</c> are never connector-managed.
/// </summary>
internal static class ConnectorDoorResolver
{
    public static async Task<Dictionary<DoorId, PacsInstance>> ResolveAsync(
        ITenantDb db, SiteId siteId, IReadOnlyCollection<Door> doors, CancellationToken ct)
    {
        var connectors = await db.PacsInstances.AsNoTracking().Where(p => p.FulfillmentMode == FulfillmentMode.Connector).ToListAsync(ct);
        var byId = connectors.ToDictionary(p => p.Id);
        var siteDefault = connectors.FirstOrDefault(p => p.SiteIds.Contains(siteId));
        var areaIds = doors.Select(d => d.AreaId).Distinct().ToList();
        var mapped = (await db.AccessLevelMappings.AsNoTracking()
                .Where(m => areaIds.Contains(m.AreaId))
                .Select(m => new { m.AreaId, m.PacsInstanceId })
                .ToListAsync(ct))
            .Where(m => byId.ContainsKey(m.PacsInstanceId))
            .GroupBy(m => m.AreaId)
            .ToDictionary(g => g.Key, g => byId[g.First().PacsInstanceId]);

        var result = new Dictionary<DoorId, PacsInstance>();
        foreach (var door in doors.Where(d => d.PacsDoorRef is not null))
        {
            if ((mapped.GetValueOrDefault(door.AreaId) ?? siteDefault) is { } instance)
            {
                result[door.Id] = instance;
            }
        }

        return result;
    }
}
