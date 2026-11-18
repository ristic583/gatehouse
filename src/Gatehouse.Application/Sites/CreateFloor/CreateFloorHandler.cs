using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Sites.CreateFloor;

/// <summary><c>POST /api/v1/admin/sites/{siteId}/floors</c>; one floor per level; audit <c>floor.created</c>.</summary>
public sealed class CreateFloorHandler(ITenantDb db, AdminAudit audit)
{
    public async Task<Result<AdminFloorDto>> HandleAsync(CreateFloorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var site = new SiteId(command.SiteId);
        if (!await db.Sites.AnyAsync(s => s.Id == site, ct))
        {
            return SiteErrors.SiteNotFound;
        }

        var floor = Floor.Create(site, request.Name?.Trim() ?? string.Empty, request.Level, request.PlanWidth, request.PlanHeight);
        if (floor.IsFailure)
        {
            return floor.Error;
        }

        if (await db.Floors.AnyAsync(f => f.SiteId == site && f.Level == request.Level, ct))
        {
            return Error.Conflict("floor.level_taken", "The site already has a floor on this level.");
        }

        db.Floors.Add(floor.Value);
        var payload = new { siteId = command.SiteId, name = floor.Value.Name, level = floor.Value.Level };
        await audit.RecordAsync("floor.created", "Floor", floor.Value.Id.Value, payload, ct);
        await db.SaveChangesAsync(ct);
        return SiteAdminMapper.ToDto(floor.Value);
    }
}
