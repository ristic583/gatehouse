using System.Security.Claims;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.LiveBuilding.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.Replay;

/// <summary>
/// <c>GET /api/v1/live/floors/{floorId}/events?from&amp;to</c> (StaffConsole + ViewLiveSite, else 404 <c>floor.not_found</c>): door events
/// of
/// one floor for the 24 h replay scrubber, oldest first, at most <see cref="MaxEvents"/>. The window must lie within the last 24 h.
/// </summary>
public sealed class FloorEventsHandler(ITenantDb db, IAuthorizationService authorization, TimeProvider time)
{
    public const int MaxEvents = 5000;
    public static readonly TimeSpan MaxWindow = TimeSpan.FromHours(24);

    public async Task<Result<IReadOnlyList<LiveDoorEventDto>>> HandleAsync(
        Guid floorId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var end = to ?? now;
        var start = from ?? end - MaxWindow;
        if (start > end || end - start > MaxWindow || start < now - MaxWindow - TimeSpan.FromMinutes(5)
            || end > now + TimeSpan.FromMinutes(5))
        {
            return Error.Validation("live.invalid_range", "The replay window must lie within the last 24 hours.");
        }

        var id = new FloorId(floorId);
        var siteId = await db.Floors.AsNoTracking().Where(f => f.Id == id).Select(f => (SiteId?)f.SiteId).SingleOrDefaultAsync(ct);
        if (siteId is null
            || !(await authorization.AuthorizeAsync(new ClaimsPrincipal(), siteId.Value, new ViewLiveSiteRequirement())).Succeeded)
        {
            return Error.NotFound("floor.not_found", "Floor not found.");
        }

        var rows = await (
                from e in db.DoorEvents.AsNoTracking()
                join d in db.Doors.AsNoTracking() on e.DoorId equals d.Id
                join a in db.Areas.AsNoTracking() on d.AreaId equals a.Id
                where a.FloorId == id && e.OccurredAt >= start && e.OccurredAt <= end
                orderby e.OccurredAt
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
            .Take(MaxEvents)
            .ToListAsync(ct);
        return rows.Select(r => new LiveDoorEventDto(r.Id, r.OccurredAt, r.Type, r.DoorId.Value, r.DoorName, r.AreaId.Value, r.AreaName,
            r.ReaderName, r.PersonId?.Value, r.PersonName, r.Direction)).ToList();
    }
}
