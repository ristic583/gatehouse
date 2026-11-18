using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.UpdatePacsInstance;

/// <summary><c>PUT /api/v1/admin/pacs-instances/{pacsInstanceId}</c>; audit <c>pacs_instance.updated</c>.</summary>
public sealed class UpdatePacsInstanceHandler(ITenantDb db, PacsAdminAudit audit, PacsInstanceAdminReadModel readModel)
{
    public async Task<Result<PacsInstanceAdminDto>> HandleAsync(UpdatePacsInstanceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var id = command.PacsInstanceId;
        var instance = await db.PacsInstances.SingleOrDefaultAsync(p => p.Id == new PacsInstanceId(id), ct);
        if (instance is null)
        {
            return PacsAdminErrors.NotFound;
        }

        var sites = await PacsInstanceRules.SitesAsync(db, request.SiteIds, ct);
        if (sites.IsFailure)
        {
            return sites.Error;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (await db.PacsInstances.AnyAsync(p => p.Name == name && p.Id != instance.Id, ct))
        {
            return Error.Conflict("pacs.name_taken", "Another PACS instance has this name.");
        }

        var registrations = await readModel.RegistrationsAsync(ct);
        var updated = instance.Update(name, request.Vendor, sites.Value, registrations.Any(r => r.PacsInstanceId == id));
        if (updated.IsFailure)
        {
            return updated.Error;
        }

        var payload = new { name, vendor = request.Vendor.ToString(), siteIds = request.SiteIds };
        await audit.RecordAsync("pacs_instance.updated", instance.Id, payload, ct);
        await db.SaveChangesAsync(ct);
        return await readModel.ToDtoAsync(instance, registrations, ct);
    }
}
