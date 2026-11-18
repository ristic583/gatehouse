using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.SetAreaOwners;

/// <summary><c>PUT /api/v1/admin/areas/{areaId}/owners</c>; at least one active employee; audit <c>area.owners_changed</c>.</summary>
public sealed class SetAreaOwnersHandler(ITenantDb db, AdminAudit audit, FloorPlanReader plans)
{
    public async Task<Result<PlanAreaDto>> HandleAsync(SetAreaOwnersCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(command.Request);
        var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == new AreaId(command.AreaId), ct);
        if (area is null)
        {
            return SiteErrors.AreaNotFound;
        }

        var owners = await AreaRules.OwnersAsync(db, command.Request.PersonIds ?? [], ct);
        if (owners.IsFailure)
        {
            return owners.Error;
        }

        var replaced = area.ReplaceOwners(owners.Value);
        if (replaced.IsFailure)
        {
            return replaced.Error;
        }

        var payload = new { ownerIds = owners.Value.Select(o => o.PersonId.Value) };
        await audit.RecordAsync("area.owners_changed", "Area", command.AreaId, payload, ct);
        await db.SaveChangesAsync(ct);
        return await plans.AreaAsync(area, ct);
    }
}
