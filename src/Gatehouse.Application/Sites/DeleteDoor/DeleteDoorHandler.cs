using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.DeleteDoor;

/// <summary><c>DELETE /api/v1/admin/doors/{doorId}</c>; audit <c>door.deleted</c>.</summary>
public sealed class DeleteDoorHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result> HandleAsync(DeleteDoorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var door = await db.Doors.SingleOrDefaultAsync(d => d.Id == new DoorId(command.DoorId), ct);
        if (door is null)
        {
            return SiteErrors.DoorNotFound;
        }

        db.Doors.Remove(door);
        await audit.RecordAsync("door.deleted", "Door", command.DoorId, new { name = door.Name, pacsDoorRef = door.PacsDoorRef }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
