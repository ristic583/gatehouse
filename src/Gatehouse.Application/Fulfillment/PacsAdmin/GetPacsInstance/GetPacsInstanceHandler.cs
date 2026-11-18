using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.GetPacsInstance;

/// <summary><c>GET /api/v1/admin/pacs-instances/{pacsInstanceId}</c>.</summary>
public sealed class GetPacsInstanceHandler(ITenantDb db, PacsInstanceAdminReadModel readModel)
{
    public async Task<Result<PacsInstanceAdminDto>> HandleAsync(GetPacsInstanceQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = new PacsInstanceId(query.PacsInstanceId);
        var instance = await db.PacsInstances.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, ct);
        return instance is null
            ? PacsAdminErrors.NotFound
            : await readModel.ToDtoAsync(instance, await readModel.RegistrationsAsync(ct), ct);
    }
}
