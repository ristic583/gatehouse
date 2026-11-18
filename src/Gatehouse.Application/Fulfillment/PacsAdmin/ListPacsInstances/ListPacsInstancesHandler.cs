using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.ListPacsInstances;

/// <summary><c>GET /api/v1/admin/pacs-instances</c>, ordered by name.</summary>
public sealed class ListPacsInstancesHandler(ITenantDb db, PacsInstanceAdminReadModel readModel)
{
    public async Task<Result<IReadOnlyList<PacsInstanceAdminDto>>> HandleAsync(ListPacsInstancesQuery query, CancellationToken ct)
    {
        var instances = await db.PacsInstances.AsNoTracking().OrderBy(p => p.Name).ToListAsync(ct);
        var registrations = await readModel.RegistrationsAsync(ct);
        var result = new List<PacsInstanceAdminDto>();
        foreach (var instance in instances)
        {
            result.Add(await readModel.ToDtoAsync(instance, registrations, ct));
        }

        return result;
    }
}
