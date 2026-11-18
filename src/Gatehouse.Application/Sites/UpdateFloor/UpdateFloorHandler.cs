using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.UpdateFloor;

/// <summary>
/// <c>PUT /api/v1/admin/floors/{floorId}</c>; shrinking below existing polygons/doors/readers is <c>floor.invalid_plan_size</c>;
/// audit <c>floor.updated</c>.
/// </summary>
public sealed class UpdateFloorHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<AdminFloorDto>> HandleAsync(UpdateFloorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var id = new FloorId(command.FloorId);
        var floor = await db.Floors.SingleOrDefaultAsync(f => f.Id == id, ct);
        if (floor is null)
        {
            return SiteErrors.FloorNotFound;
        }

        if (request.Level != floor.Level
            && await db.Floors.AnyAsync(f => f.SiteId == floor.SiteId && f.Level == request.Level && f.Id != id, ct))
        {
            return Error.Conflict("floor.level_taken", "The site already has a floor on this level.");
        }

        var (maxX, maxY) = await GeometryExtentAsync(id, ct);
        if (request.PlanWidth < maxX || request.PlanHeight < maxY)
        {
            return Error.Validation("floor.invalid_plan_size", "The plan would be smaller than existing areas, doors or readers.");
        }

        var updated = floor.Update(request.Name?.Trim() ?? string.Empty, request.Level, request.PlanWidth, request.PlanHeight);
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var payload = new { name = floor.Name, level = floor.Level, planWidth = floor.PlanWidth, planHeight = floor.PlanHeight };
        await audit.RecordAsync("floor.updated", "Floor", command.FloorId, payload, ct);
        await db.SaveChangesAsync(ct);
        return SiteAdminMapper.ToDto(floor);
    }

    private async Task<(double MaxX, double MaxY)> GeometryExtentAsync(FloorId floorId, CancellationToken ct)
    {
        var areas = await db.Areas.AsNoTracking().Where(a => a.FloorId == floorId).ToListAsync(ct);
        var areaIds = areas.Select(a => a.Id).ToList();
        var doors = await db.Doors.AsNoTracking().Where(d => areaIds.Contains(d.AreaId)).ToListAsync(ct);
        var doorIds = doors.Select(d => d.Id).ToList();
        var readers = await db.Readers.AsNoTracking().Where(r => doorIds.Contains(r.DoorId)).ToListAsync(ct);
        var points = areas.SelectMany(a => a.Polygon.Points)
            .Concat(doors.Select(d => d.Position))
            .Concat(readers.Select(r => r.Position))
            .ToList();
        return points.Count == 0 ? (0, 0) : (points.Max(p => p.X), points.Max(p => p.Y));
    }
}
