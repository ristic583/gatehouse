using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.GetArea;

public sealed class GetAreaHandler(ITenantDb db)
{
    public async Task<Result<AreaDetailsDto>> HandleAsync(GetAreaQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var areaId = new AreaId(query.AreaId);
        var area = await db.Areas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == areaId, ct);
        if (area is null)
        {
            return Error.NotFound("area.not_found", "Area not found.");
        }

        var floor = await db.Floors.AsNoTracking().SingleAsync(f => f.Id == area.FloorId, ct);
        var level = await db.SecurityLevels.AsNoTracking().SingleAsync(l => l.Id == area.SecurityLevelId, ct);
        var ownerIds = area.Owners.Select(o => o.PersonId).ToList();
        var owners = await db.Persons.AsNoTracking()
            .Where(p => ownerIds.Contains(p.Id))
            .OrderBy(p => p.GivenName).ThenBy(p => p.FamilyName)
            .Select(p => new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName))
            .ToListAsync(ct);

        return new AreaDetailsDto(
            area.Id.Value,
            area.Name,
            area.Code,
            floor.Id.Value,
            floor.SiteId.Value,
            new SecurityLevelDto(level.Id.Value, level.Name, level.ApproverChain.ToString(), level.MaxDurationDays),
            owners);
    }
}
