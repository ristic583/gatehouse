using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UpdateDoor;

/// <summary><c>PUT /api/v1/admin/doors/{doorId}</c>: may move to an area of the same floor; audit <c>door.updated</c>.</summary>
public sealed class UpdateDoorHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<PlanDoorDto>> HandleAsync(UpdateDoorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var door = await db.Doors.SingleOrDefaultAsync(d => d.Id == new DoorId(command.DoorId), ct);
        if (door is null)
        {
            return SiteErrors.DoorNotFound;
        }

        var floor = await DoorPlacement.FloorOfAreaAsync(db, door.AreaId, ct);
        if (request.AreaId is { } target && target != door.AreaId.Value)
        {
            var targetArea = await db.Areas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == new AreaId(target), ct);
            if (targetArea is null || targetArea.FloorId != floor.Id)
            {
                return Error.Validation("door.invalid_area", "The door can only move to an area of the same floor.");
            }

            door.MoveTo(targetArea.Id);
        }

        var position = DoorPlacement.Position(request.Position);
        if (!DoorPlacement.Inside(floor, position))
        {
            return Error.Validation("door.invalid_position", "The door must lie inside the floor plan.");
        }

        var pacsRef = DoorPlacement.Normalize(request.PacsDoorRef);
        if (pacsRef is not null && await DoorPlacement.RefTakenAsync(db, floor.SiteId, pacsRef, door.Id, ct))
        {
            return Error.Conflict("door.ref_taken", "Another door of the same PACS instance uses this reference.");
        }

        var updated = door.Update(request.Name?.Trim() ?? string.Empty, position, pacsRef);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var payload = new { name = door.Name, pacsDoorRef = pacsRef, areaId = door.AreaId.Value };
        await audit.RecordAsync("door.updated", "Door", command.DoorId, payload, ct);
        await db.SaveChangesAsync(ct);
        var readers = await db.Readers.AsNoTracking().Where(r => r.DoorId == door.Id).OrderBy(r => r.Name).ToListAsync(ct);
        return FloorPlanReader.Door(door, readers);
    }
}
