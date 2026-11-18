using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.DeletePacsInstance;

/// <summary>
/// <c>DELETE /api/v1/admin/pacs-instances/{pacsInstanceId}</c>: not while a connector registration or fulfillments exist;
/// removes its mappings; audit <c>pacs_instance.deleted</c>.
/// </summary>
public sealed class DeletePacsInstanceHandler(ITenantDb db, PacsAdminAudit audit, PacsInstanceAdminReadModel readModel)
{
    public async Task<Result> HandleAsync(DeletePacsInstanceCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = command.PacsInstanceId;
        var pacsId = new PacsInstanceId(id);
        var instance = await db.PacsInstances.SingleOrDefaultAsync(p => p.Id == pacsId, ct);
        if (instance is null)
        {
            return PacsAdminErrors.NotFound;
        }

        var registrations = await readModel.RegistrationsAsync(ct);
        if (registrations.Any(r => r.PacsInstanceId == id)
            || await db.Fulfillments.AnyAsync(f => f.PacsInstanceId == pacsId, ct))
        {
            return Error.Conflict("pacs.in_use", "The PACS instance has a connector registration or fulfillments.");
        }

        db.AccessLevelMappings.RemoveRange(await db.AccessLevelMappings.Where(m => m.PacsInstanceId == pacsId).ToListAsync(ct));
        db.PacsInstances.Remove(instance);
        await audit.RecordAsync("pacs_instance.deleted", pacsId, new { name = instance.Name }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
