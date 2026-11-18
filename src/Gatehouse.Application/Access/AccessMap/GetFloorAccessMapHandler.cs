using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.AccessMap;

/// <summary>Every area of a floor with the current user's access state (domain-model.md §3). Six queries, no N+1.</summary>
public sealed class GetFloorAccessMapHandler(ITenantDb db, ICurrentUser currentUser, IConnectorHealthReader health, TimeProvider time)
{
    public async Task<Result<FloorAccessMapDto>> HandleAsync(GetFloorAccessMapQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var floorId = new FloorId(query.FloorId);

        var floor = await db.Floors.AsNoTracking().SingleOrDefaultAsync(f => f.Id == floorId, ct);
        if (floor is null)
        {
            return Error.NotFound("floor.not_found", "Floor not found.");
        }

        var areas = await db.Areas.AsNoTracking()
            .Where(a => a.FloorId == floorId)
            .OrderBy(a => a.Name)
            .Join(
                db.SecurityLevels, a => a.SecurityLevelId, l => l.Id, (a, l) => new { a.Id, a.Name, a.Code, a.Polygon, LevelName = l.Name })
            .ToListAsync(ct);
        var areaIds = areas.Select(a => a.Id).ToList();

        var doors = await db.Doors.AsNoTracking().Where(d => areaIds.Contains(d.AreaId)).OrderBy(d => d.Name).ToListAsync(ct);
        var mappings = await db.AccessLevelMappings.AsNoTracking()
            .Where(m => areaIds.Contains(m.AreaId))
            .Select(m => new { m.AreaId, m.PacsInstanceId })
            .ToListAsync(ct);
        var person = await db.Persons.AsNoTracking()
            .Where(p => p.Id == me)
            .Select(p => new { p.Status, p.Eligibility.Start, p.Eligibility.End })
            .SingleAsync(ct);
        var entitlements = await db.Entitlements.AsNoTracking()
            .Where(e => e.PersonId == me && areaIds.Contains(e.AreaId))
            .Select(e => new
            {
                e.AreaId,
                e.Status,
                e.RequestedStart,
                e.EffectiveEnd,
                Fulfillments = db.Fulfillments.Where(f => f.EntitlementId == e.Id).Select(f => new { f.Kind, f.Status }).ToList(),
            })
            .ToListAsync(ct);

        var healthById = await health.GetAsync(mappings.Select(m => m.PacsInstanceId).Distinct().ToList(), ct);
        var eligibility = new DateRange(person.Start, person.End);

        var result = areas.Select(area =>
        {
            var instances = mappings.Where(m => m.AreaId == area.Id).Select(m => m.PacsInstanceId).ToList();
            var offline = instances
                .Select(id => healthById.TryGetValue(id, out var h) ? h : new ConnectorHealth(false, null))
                .Where(h => !h.Online)
                .ToList();
            var snapshots = entitlements
                .Where(e => e.AreaId == area.Id)
                .Select(e =>
                {
                    var grants = e.Fulfillments.Where(f => f.Kind == FulfillmentKind.Grant).ToList();
                    var revokes = e.Fulfillments.Where(f => f.Kind == FulfillmentKind.Revoke).ToList();
                    return new EntitlementSnapshot(
                        e.Status,
                        e.RequestedStart,
                        e.EffectiveEnd,
                        grants.Count > 0 && grants.All(f => f.Status == FulfillmentStatus.Confirmed),
                        e.Status == EntitlementStatus.Revoked && revokes.Any(f => f.Status != FulfillmentStatus.Confirmed));
                })
                .ToList();

            var state = AreaAccessStateCalculator.Calculate(new AreaAccessInput(
                now,
                eligibility,
                person.Status == PersonStatus.Active,
                instances.Count > 0,
                offline.Where(h => h.LastHeartbeatAt is not null).Select(h => h.LastHeartbeatAt).Min(),
                offline.Count > 0,
                snapshots,
                null));

            return new AreaAccessDto(
                area.Id.Value,
                area.Name,
                area.Code,
                area.Polygon.Points.Select(p => new PointDto(p.X, p.Y)).ToList(),
                area.LevelName,
                state.State.ToString(),
                state.Reason.ToString(),
                state.CardInactive,
                state.Since,
                doors.Where(d => d.AreaId == area.Id)
                    .Select(d => new DoorMarkerDto(d.Id.Value, d.Name, d.Position.X, d.Position.Y))
                    .ToList());
        }).ToList();

        return new FloorAccessMapDto(
            floor.Id.Value, floor.SiteId.Value, floor.Name, floor.PlanWidth, floor.PlanHeight, result, floor.HasBackground,
            floor.BackgroundUpdatedAt);
    }
}
