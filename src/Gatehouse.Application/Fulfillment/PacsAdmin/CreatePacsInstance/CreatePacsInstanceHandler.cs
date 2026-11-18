using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.CreatePacsInstance;

/// <summary><c>POST /api/v1/admin/pacs-instances</c>: name unique, at least one site; audit <c>pacs_instance.created</c>.</summary>
public sealed class CreatePacsInstanceHandler(ITenantDb db, PacsAdminAudit audit, PacsInstanceAdminReadModel readModel)
{
    public async Task<Result<PacsInstanceAdminDto>> HandleAsync(CreatePacsInstanceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        ArgumentNullException.ThrowIfNull(request);
        var sites = await PacsInstanceRules.SitesAsync(db, request.SiteIds, ct);
        if (sites.IsFailure)
        {
            return sites.Error;
        }

        var name = request.Name?.Trim() ?? string.Empty;
        if (await db.PacsInstances.AnyAsync(p => p.Name == name, ct))
        {
            return Error.Conflict("pacs.name_taken", "Another PACS instance has this name.");
        }

        var instance = PacsInstance.Create(name, request.Vendor, sites.Value);
        if (instance.IsFailure)
        {
            return instance.Error;
        }

        db.PacsInstances.Add(instance.Value);
        await audit.RecordAsync("pacs_instance.created", instance.Value.Id, new { name, vendor = request.Vendor.ToString() }, ct);
        await db.SaveChangesAsync(ct);
        return await readModel.ToDtoAsync(instance.Value, [], ct);
    }
}
