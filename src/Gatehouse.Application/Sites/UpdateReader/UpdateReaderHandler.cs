using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UpdateReader;

/// <summary><c>PUT /api/v1/admin/readers/{readerId}</c>: inside the plan; audit <c>reader.updated</c>.</summary>
public sealed class UpdateReaderHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<PlanReaderDto>> HandleAsync(UpdateReaderCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var reader = await db.Readers.SingleOrDefaultAsync(r => r.Id == new ReaderId(command.ReaderId), ct);
        if (reader is null)
        {
            return SiteErrors.ReaderNotFound;
        }

        var door = await db.Doors.AsNoTracking().SingleAsync(d => d.Id == reader.DoorId, ct);
        var floor = await DoorPlacement.FloorOfAreaAsync(db, door.AreaId, ct);
        var position = DoorPlacement.Position(request.Position);
        if (!DoorPlacement.Inside(floor, position))
        {
            return Error.Validation("reader.invalid_position", "The reader must lie inside the floor plan.");
        }

        var name = request.Name?.Trim() ?? string.Empty;
        var updated = reader.Update(name, request.Direction, position, DoorPlacement.Normalize(request.PacsReaderRef));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        await audit.RecordAsync("reader.updated", "Reader", command.ReaderId, new { name = reader.Name }, ct);
        await db.SaveChangesAsync(ct);
        return FloorPlanReader.Reader(reader);
    }
}
