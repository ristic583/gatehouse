using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.DeleteArea;

/// <summary>
/// <c>DELETE /api/v1/admin/areas/{areaId}</c>: not while entitlements or mappings reference it; audit <c>area.deleted</c>.
/// </summary>
public sealed class DeleteAreaHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result> HandleAsync(DeleteAreaCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = new AreaId(command.AreaId);
        var area = await db.Areas.SingleOrDefaultAsync(a => a.Id == id, ct);
        if (area is null)
        {
            return SiteErrors.AreaNotFound;
        }

        if (await db.Entitlements.AnyAsync(e => e.AreaId == id, ct) || await db.AccessLevelMappings.AnyAsync(m => m.AreaId == id, ct))
        {
            return Error.Conflict("area.in_use", "The area is referenced by entitlements or access-level mappings.");
        }

        db.Areas.Remove(area);
        await audit.RecordAsync("area.deleted", "Area", command.AreaId, new { name = area.Name, code = area.Code }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
