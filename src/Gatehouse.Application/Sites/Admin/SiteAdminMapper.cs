using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Sites;

namespace Gatehouse.Application.Sites.Admin;

/// <summary>DTOs returned by the site and floor admin handlers.</summary>
public static class SiteAdminMapper
{
    public static SiteDto ToDto(Site site, IEnumerable<Floor> floors)
    {
        ArgumentNullException.ThrowIfNull(site);
        return new SiteDto(
            site.Id.Value, site.Name, site.Code, site.TimeZoneId,
            floors.Select(f => new FloorSummaryDto(f.Id.Value, f.Name, f.Level)).ToList());
    }

    public static AdminFloorDto ToDto(Floor floor)
    {
        ArgumentNullException.ThrowIfNull(floor);
        return new AdminFloorDto(floor.Id.Value, floor.SiteId.Value, floor.Name, floor.Level, floor.PlanWidth, floor.PlanHeight);
    }
}
