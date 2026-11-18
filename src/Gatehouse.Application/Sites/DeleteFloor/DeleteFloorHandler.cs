using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.DeleteFloor;

/// <summary>
/// <c>DELETE /api/v1/admin/floors/{floorId}</c>: only without areas; deletes the background after commit; audit <c>floor.deleted</c>.
/// </summary>
public sealed class DeleteFloorHandler(ITenantDb db, AdminAudit audit, IObjectStore store)
{
    public async Task<Result> HandleAsync(DeleteFloorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = new FloorId(command.FloorId);
        var floor = await db.Floors.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        if (await db.Areas.AnyAsync(a => a.FloorId == id, ct))
        {
            return Error.Conflict("floor.not_empty", "Delete the floor's areas first.");
        }

        var background = floor.BackgroundObjectKey;
        db.Floors.Remove(floor);
        var payload = new { siteId = floor.SiteId.Value, name = floor.Name, level = floor.Level };
        await audit.RecordAsync("floor.deleted", "Floor", command.FloorId, payload, ct);
        await db.SaveChangesAsync(ct);
        if (background is not null)
        {
            await store.DeleteAsync(background, ct);
        }
        return Result.Success();
    }
}
