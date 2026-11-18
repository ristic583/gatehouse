using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.CreateReader;

/// <summary><c>POST /api/v1/admin/doors/{doorId}/readers</c>: inside the plan; audit <c>reader.created</c>.</summary>
public sealed class CreateReaderHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<PlanReaderDto>> HandleAsync(CreateReaderCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var door = await db.Doors.AsNoTracking().SingleOrDefaultAsync(d => d.Id == new DoorId(command.DoorId), ct);
        if (door is null)
        {
            return SiteErrors.DoorNotFound;
        }

        var floor = await DoorPlacement.FloorOfAreaAsync(db, door.AreaId, ct);
        var position = DoorPlacement.Position(request.Position);
        if (!DoorPlacement.Inside(floor, position))
        {
            return Error.Validation("reader.invalid_position", "The reader must lie inside the floor plan.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var reader = Reader.Create(door.Id, name, request.Direction, position, DoorPlacement.Normalize(request.PacsReaderRef));
        if (reader.IsFailure)
        {
            return reader.Error;
        }

        db.Readers.Add(reader.Value);
        var payload = new { doorId = command.DoorId, name = reader.Value.Name };
        await audit.RecordAsync("reader.created", "Reader", reader.Value.Id.Value, payload, ct);
        await db.SaveChangesAsync(ct);
        return FloorPlanReader.Reader(reader.Value);
    }
}
