using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Photos;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.RemoveFloorBackground;

/// <summary>
/// <c>DELETE /api/v1/admin/floors/{floorId}/background</c>; deletes the object after commit; audit <c>floor.background_removed</c>.
/// </summary>
public sealed class RemoveFloorBackgroundHandler(ITenantDb db, IObjectStore store, AdminAudit audit)
{
    public async Task<Result> HandleAsync(RemoveFloorBackgroundCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var floor = await db.Floors.SingleOrDefaultAsync(f => f.Id == new FloorId(command.FloorId), ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        var previous = floor.BackgroundObjectKey;
        if (previous is null)
        {
            return SiteErrors.BackgroundNotFound;
        }

        floor.ClearBackground();
        await audit.RecordAsync("floor.background_removed", "Floor", command.FloorId, null, ct);
        await db.SaveChangesAsync(ct);
        await store.DeleteAsync(previous, ct);
        return Result.Success();
    }
}
