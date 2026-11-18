using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;

/// <summary>
/// <c>PUT /api/v1/admin/pacs-instances/{pacsInstanceId}/mappings</c>: replaces the mapping set; every area must lie on one of
/// the instance's sites. A changed level plans grant migration (M11-16); audit <c>pacs_instance.mappings_changed</c> and
/// <c>pacs_instance.grants_migrated</c>.
/// </summary>
public sealed class SetAccessLevelMappingsHandler(
    ITenantDb db,
    ICurrentUser user,
    IAuditLog audit,
    TimeProvider time,
    GrantMigrationService migration,
    PacsInstanceAdminReadModel readModel)
{
    public async Task<Result<PacsInstanceAdminDto>> HandleAsync(SetAccessLevelMappingsCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Mappings;
        ArgumentNullException.ThrowIfNull(request);
        var pacsId = new PacsInstanceId(command.PacsInstanceId);
        var instance = await db.PacsInstances.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pacsId, ct);
        if (instance is null)
        {
            return PacsAdminErrors.NotFound;
        }

        var areaIds = request.Select(r => new AreaId(r.AreaId)).ToList();
        var siteIds = instance.SiteIds.ToList();
        var areas = await (from a in db.Areas.AsNoTracking()
                           join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                           where areaIds.Contains(a.Id)
                           select new { a.Id, a.Name, f.SiteId }).ToListAsync(ct);
        if (areas.Count != areaIds.Count || areas.Any(a => !siteIds.Contains(a.SiteId)))
        {
            return Error.Validation("mapping.area_not_on_instance_site", "Every mapped area must be on one of the instance's sites.");
        }

        var existing = await db.AccessLevelMappings.Where(m => m.PacsInstanceId == pacsId).ToListAsync(ct);
        db.AccessLevelMappings.RemoveRange(existing.Where(m => !areaIds.Contains(m.AreaId)));
        var actor = AuditActor.ForPerson(await user.RequirePersonIdAsync(ct));
        var now = time.GetUtcNow();
        var (migrated, areasMigrated) = (0, 0);
        foreach (var r in request)
        {
            var areaId = new AreaId(r.AreaId);
            var display = string.IsNullOrWhiteSpace(r.DisplayName) ? r.VendorAccessLevelRef : r.DisplayName.Trim();
            var current = existing.FirstOrDefault(m => m.AreaId == areaId);
            var (fromLevel, fromDisplay) = (current?.VendorAccessLevelRef, current?.DisplayName);
            var vendorRef = r.VendorAccessLevelRef?.Trim() ?? string.Empty;
            var outcome = current is null
                ? AddMapping(areaId, pacsId, vendorRef, display ?? string.Empty)
                : current.Update(vendorRef, display ?? string.Empty);
            if (outcome.IsFailure)
            {
                return outcome.Error;
            }

            // A changed level moves active grants (revoke old now, grant new after the revoke is out); a deleted mapping keeps them.
            if (current is not null && fromLevel != current.VendorAccessLevelRef)
            {
                var planned = await migration.PlanRevokesAsync(
                    pacsId, areaId, fromLevel!, fromDisplay!, current.VendorAccessLevelRef, actor, now, ct);
                (migrated, areasMigrated) = (migrated + planned, areasMigrated + (planned > 0 ? 1 : 0));
            }
        }

        audit.Record(actor, "pacs_instance.mappings_changed", "PacsInstance", pacsId.Value.ToString(), new { count = request.Count });
        if (migrated > 0)
        {
            audit.Record(actor, "pacs_instance.grants_migrated", "PacsInstance", pacsId.Value.ToString(),
                new { areas = areasMigrated, grants = migrated });
        }

        await db.SaveChangesAsync(ct);
        return await readModel.ToDtoAsync(instance, await readModel.RegistrationsAsync(ct), ct);
    }

    private Result AddMapping(AreaId areaId, PacsInstanceId pacsId, string vendorRef, string display)
    {
        var mapping = AccessLevelMapping.Create(areaId, pacsId, vendorRef, display);
        if (mapping.IsFailure)
        {
            return mapping.Error;
        }

        db.AccessLevelMappings.Add(mapping.Value);
        return Result.Success();
    }
}
