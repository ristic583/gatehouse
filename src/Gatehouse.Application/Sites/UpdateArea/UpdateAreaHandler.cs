using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UpdateArea;

/// <summary><c>PUT /api/v1/admin/areas/{areaId}</c>; <c>null</c> owners keep the owners; audit <c>area.updated</c>.</summary>
public sealed class UpdateAreaHandler(ITenantDb db, AdminAudit audit, FloorPlanReader plans)
{
    public async Task<Result<PlanAreaDto>> HandleAsync(UpdateAreaCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == new AreaId(command.AreaId), ct);
        if (area is null)
        {
            return SiteErrors.AreaNotFound;
        }

        var floor = await db.Floors.AsNoTracking().SingleAsync(f => f.Id == area.FloorId, ct);
        var points = (request.Polygon ?? []).Select(p => new Point(p.X, p.Y));
        var updated = area.Update(request.Name?.Trim() ?? string.Empty, request.Code?.Trim() ?? string.Empty,
            points, new SecurityLevelId(request.SecurityLevelId), floor.PlanWidth, floor.PlanHeight);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        if (request.AccessControlled is { } accessControlled)
        {
            area.SetAccessControlled(accessControlled);
        }

        if (request.OwnerIds is { } ownerIds)
        {
            var owners = await AreaRules.OwnersAsync(db, ownerIds, ct);
            if (owners.IsFailure)
            {
                return owners.Error;
            }

            var replaced = area.ReplaceOwners(owners.Value);
            if (replaced.IsFailure)
            {
                return replaced.Error;
            }
        }

        var rules = await AreaRules.CheckAsync(db, area, area.Id, ct);
        if (rules.IsFailure)
        {
            return rules.Error;
        }

        // Doors and readers must stay inside the plan; the polygon moves but door positions are independent geometry.
        var payload = new { name = area.Name, code = area.Code, securityLevelId = area.SecurityLevelId.Value };
        await audit.RecordAsync("area.updated", "Area", command.AreaId, payload, ct);
        await db.SaveChangesAsync(ct);
        return await plans.AreaAsync(area, ct);
    }
}
