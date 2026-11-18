using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.Admin;

/// <summary><c>GET /api/v1/admin/floors/{floorId}/plan</c> and the per-element DTOs returned by admin edits.</summary>
public sealed class FloorPlanReader(ITenantDb db)
{
    public async Task<Result<FloorPlanDto>> GetAsync(Guid floorId, CancellationToken ct)
    {
        var id = new FloorId(floorId);
        var floor = await db.Floors.AsNoTracking().SingleOrDefaultAsync(f => f.Id == id, ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        var areas = await db.Areas.AsNoTracking().Where(a => a.FloorId == id).OrderBy(a => a.Name).ToListAsync(ct);
        var dtos = new List<PlanAreaDto>();
        foreach (var area in areas)
        {
            dtos.Add(await AreaAsync(area, ct));
        }

        return new FloorPlanDto(
            floor.Id.Value, floor.SiteId.Value, floor.Name, floor.Level, floor.PlanWidth, floor.PlanHeight, dtos,
            floor.HasBackground, floor.BackgroundUpdatedAt);
    }

    public async Task<PlanAreaDto> AreaAsync(Area area, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(area);
        var ownerIds = area.Owners.Select(o => o.PersonId).ToList();
        var owners = await db.Persons.AsNoTracking().Where(p => ownerIds.Contains(p.Id))
            .OrderBy(p => p.FamilyName).ThenBy(p => p.GivenName)
            .Select(p => new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName))
            .ToListAsync(ct);
        var areaId = area.Id;
        var doors = await db.Doors.AsNoTracking().Where(d => d.AreaId == areaId).OrderBy(d => d.Name).ToListAsync(ct);
        var doorIds = doors.Select(d => d.Id).ToList();
        var readers = await db.Readers.AsNoTracking().Where(r => doorIds.Contains(r.DoorId)).OrderBy(r => r.Name).ToListAsync(ct);
        return new PlanAreaDto(
            area.Id.Value, area.Name, area.Code, area.Polygon.Points.Select(ToDto).ToList(), area.SecurityLevelId.Value, owners,
            doors.Select(d => Door(d, readers.Where(r => r.DoorId == d.Id))).ToList(), area.AccessControlled);
    }

    public static PlanDoorDto Door(Door door, IEnumerable<Reader> readers)
    {
        ArgumentNullException.ThrowIfNull(door);
        return new PlanDoorDto(door.Id.Value, door.Name, ToDto(door.Position), door.PacsDoorRef, readers.Select(Reader).ToList());
    }

    public static PlanReaderDto Reader(Reader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);
        return new PlanReaderDto(reader.Id.Value, reader.Name, reader.Direction, ToDto(reader.Position), reader.PacsReaderRef);
    }

    public static PointDto ToDto(Point p) => new(p.X, p.Y);
}
