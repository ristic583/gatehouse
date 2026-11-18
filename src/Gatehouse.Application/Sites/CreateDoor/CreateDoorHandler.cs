using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.CreateDoor;

/// <summary>
/// <c>POST /api/v1/admin/areas/{areaId}/doors</c>: inside the plan, reference unique per PACS instance; audit <c>door.created</c>.
/// </summary>
public sealed class CreateDoorHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<PlanDoorDto>> HandleAsync(CreateDoorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var area = await db.Areas.AsNoTracking().SingleOrDefaultAsync(a => a.Id == new AreaId(command.AreaId), ct);
        if (area is null)
        {
            return SiteErrors.AreaNotFound;
        }

        var floor = await db.Floors.AsNoTracking().SingleAsync(f => f.Id == area.FloorId, ct);
        var position = DoorPlacement.Position(request.Position);
        if (!DoorPlacement.Inside(floor, position))
        {
            return Error.Validation("door.invalid_position", "The door must lie inside the floor plan.");
        }

        var pacsRef = DoorPlacement.Normalize(request.PacsDoorRef);
        if (pacsRef is not null && await DoorPlacement.RefTakenAsync(db, floor.SiteId, pacsRef, null, ct))
        {
            return Error.Conflict("door.ref_taken", "Another door of the same PACS instance uses this reference.");
        }

        var door = Door.Create(area.Id, request.Name?.Trim() ?? string.Empty, position, pacsRef);
        if (door.IsFailure)
        {
            return door.Error;
        }

        db.Doors.Add(door.Value);
        var payload = new { areaId = command.AreaId, name = door.Value.Name, pacsDoorRef = pacsRef };
        await audit.RecordAsync("door.created", "Door", door.Value.Id.Value, payload, ct);
        await db.SaveChangesAsync(ct);
        return FloorPlanReader.Door(door.Value, []);
    }
}
