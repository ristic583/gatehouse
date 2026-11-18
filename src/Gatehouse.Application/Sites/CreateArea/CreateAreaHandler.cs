using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.CreateArea;

/// <summary><c>POST /api/v1/admin/floors/{floorId}/areas</c> (<c>domain-model.md</c> §2.1, §2.10); audit <c>area.created</c>.</summary>
public sealed class CreateAreaHandler(ITenantDb db, AdminAudit audit, FloorPlanReader plans)
{
    public async Task<Result<PlanAreaDto>> HandleAsync(CreateAreaCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var floor = await db.Floors.AsNoTracking().SingleOrDefaultAsync(f => f.Id == new FloorId(command.FloorId), ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        if (request.OwnerIds is not { Count: > 0 })
        {
            return Error.Conflict("area.last_owner", "An area needs at least one owner.");
        }

        var owners = await AreaRules.OwnersAsync(db, request.OwnerIds, ct);
        if (owners.IsFailure)
        {
            return owners.Error;
        }

        var polygon = Polygon.Create((request.Polygon ?? []).Select(p => new Point(p.X, p.Y)), floor.PlanWidth, floor.PlanHeight);
        if (polygon.IsFailure)
        {
            return polygon.Error;
        }

        var first = owners.Value[0];
        var area = Area.Create(floor.Id, request.Name?.Trim() ?? string.Empty, request.Code?.Trim() ?? string.Empty, polygon.Value,
            new SecurityLevelId(request.SecurityLevelId), first.PersonId, first.Type);
        if (area.IsFailure)
        {
            return area.Error;
        }

        area.Value.SetAccessControlled(request.AccessControlled ?? true);
        var replaced = area.Value.ReplaceOwners(owners.Value);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var rules = await AreaRules.CheckAsync(db, area.Value, null, ct);
        if (rules.IsFailure)
        {
            return rules.Error;
        }

        db.Areas.Add(area.Value);
        var payload = new { floorId = command.FloorId, name = area.Value.Name, code = area.Value.Code };
        await audit.RecordAsync("area.created", "Area", area.Value.Id.Value, payload, ct);
        await db.SaveChangesAsync(ct);
        return await plans.AreaAsync(area.Value, ct);
    }
}
