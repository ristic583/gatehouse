using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.GetLiveFloor;

/// <summary>Floor snapshot for the live view (authorization.md §9: <c>ViewLiveSiteRequirement</c>, else 404).</summary>
public sealed class GetLiveFloorHandler(
    ITenantDb db, DoorStateReader stateReader, IOccupancyStore occupancy, IAuthorizationService authorization, TimeProvider time)
{
    public const int RecentEventCount = 50;

    public async Task<Result<LiveFloorDto>> HandleAsync(GetLiveFloorQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var floorId = new FloorId(query.FloorId);
        var floor = await db.Floors.AsNoTracking()
            .Where(f => f.Id == floorId)
            .Join(
                db.Sites, f => f.SiteId, s => s.Id,
                (f, s) => new
                {
                    f.Id,
                    f.SiteId,
                    SiteName = s.Name,
                    s.TimeZoneId,
                    f.Name,
                    f.PlanWidth,
                    f.PlanHeight,
                    f.BackgroundObjectKey,
                    f.BackgroundUpdatedAt,
                })
            .SingleOrDefaultAsync(ct);
        if (floor is null
            || !(await authorization.AuthorizeAsync(new ClaimsPrincipal(), floor.SiteId, new ViewLiveSiteRequirement())).Succeeded)
        {
            return Error.NotFound("floor.not_found", "Floor not found.");
        }

        var areas = await db.Areas.AsNoTracking().Where(a => a.FloorId == floorId).OrderBy(a => a.Name).ToListAsync(ct);
        var areaIds = areas.Select(a => a.Id).ToList();
        var areaNames = areas.ToDictionary(a => a.Id, a => a.Name);
        var doors = await db.Doors.AsNoTracking().Where(d => areaIds.Contains(d.AreaId)).OrderBy(d => d.Name).ToListAsync(ct);
        var doorIds = doors.Select(d => d.Id).ToList();
        var readers = (await db.Readers.AsNoTracking().Where(r => doorIds.Contains(r.DoorId)).OrderBy(r => r.Name).ToListAsync(ct))
            .ToLookup(r => r.DoorId);
        var connectorDoors = await ConnectorDoorResolver.ResolveAsync(db, floor.SiteId, doors, ct);
        var states = await stateReader.GetAsync(doorIds, ct);

        var doorDtos = doors.Select(d =>
        {
            var state = states.GetValueOrDefault(d.Id);
            var instance = connectorDoors.GetValueOrDefault(d.Id);
            return new LiveDoorDto(
                d.Id.Value,
                d.AreaId.Value,
                d.Name,
                new PointDto(d.Position.X, d.Position.Y),
                d.PacsDoorRef,
                instance is not null,
                state?.State ?? DoorState.Locked,
                state?.Since,
                readers[d.Id].Select(r => new LiveReaderDto(r.Id.Value, r.Name, new PointDto(r.Position.X, r.Position.Y))).ToList(),
                instance is not null
                    && PacsCapabilityGaps.Supports(
                        instance.CapabilitiesJson, Gatehouse.Connector.Contracts.Adapters.DoorCommandKind.MomentaryUnlock),
                instance is not null
                    && PacsCapabilityGaps.Supports(
                        instance.CapabilitiesJson, Gatehouse.Connector.Contracts.Adapters.DoorCommandKind.Lockdown));
        }).ToList();

        var doorById = doors.ToDictionary(d => d.Id);
        var alarms = (await db.Alarms.AsNoTracking()
                .Where(a => doorIds.Contains(a.DoorId) && a.AcknowledgedAt == null)
                .OrderByDescending(a => a.RaisedAt)
                .ToListAsync(ct))
            .Select(a =>
            {
                var door = doorById[a.DoorId];
                return new AlarmDto(a.Id.Value, a.DoorId.Value, door.Name, door.AreaId.Value, areaNames[door.AreaId], floor.SiteId.Value,
                    a.Kind, a.RaisedAt, a.DoorEventId, null, null, null);
            })
            .ToList();

        var counts = await occupancy.GetAsync(
            areas.Select(a => a.Id).ToList(), IngestDoorEvents.IngestDoorEventsHandler.LocalDate(time.GetUtcNow(), floor.TimeZoneId), ct);
        return new LiveFloorDto(floor.Id.Value, floor.SiteId.Value, floor.SiteName, floor.Name, floor.PlanWidth, floor.PlanHeight,
            areas.Select(a => new LiveAreaDto(a.Id.Value, a.Name, a.Code, a.Polygon.Points.Select(p => new PointDto(p.X, p.Y)).ToList()))
                .ToList(),
            doorDtos,
            alarms,
            await RecentEventsAsync(floor.SiteId, ct),
            areas.Select(a => new OccupancyDto(a.Id.Value, counts.GetValueOrDefault(a.Id))).ToList(),
            floor.BackgroundObjectKey is not null,
            floor.BackgroundUpdatedAt);
    }

    /// <summary>Last events on the site within the rebuild window (keeps the partitioned scan small).</summary>
    private async Task<List<LiveDoorEventDto>> RecentEventsAsync(SiteId siteId, CancellationToken ct)
    {
        var since = time.GetUtcNow() - DoorStateReader.RebuildWindow;
        var rows = await (
                from e in db.DoorEvents.AsNoTracking()
                join d in db.Doors.AsNoTracking() on e.DoorId equals d.Id
                join a in db.Areas.AsNoTracking() on d.AreaId equals a.Id
                join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                where f.SiteId == siteId && e.OccurredAt >= since
                orderby e.OccurredAt descending
                select new
                {
                    e.Id,
                    e.OccurredAt,
                    e.Type,
                    DoorId = d.Id,
                    DoorName = d.Name,
                    AreaId = a.Id,
                    AreaName = a.Name,
                    ReaderName = db.Readers.Where(r => r.Id == e.ReaderId).Select(r => r.Name).FirstOrDefault(),
                    e.PersonId,
                    PersonName = db.Persons.Where(p => p.Id == e.PersonId).Select(p => p.GivenName + " " + p.FamilyName).FirstOrDefault(),
                    e.Direction,
                })
            .Take(RecentEventCount)
            .ToListAsync(ct);
        return rows.Select(r => new LiveDoorEventDto(r.Id, r.OccurredAt, r.Type, r.DoorId.Value, r.DoorName, r.AreaId.Value, r.AreaName,
            r.ReaderName, r.PersonId?.Value, r.PersonName, r.Direction)).ToList();
    }
}
