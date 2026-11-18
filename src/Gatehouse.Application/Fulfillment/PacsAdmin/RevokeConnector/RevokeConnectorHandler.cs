using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.RevokeConnector;

/// <summary><c>POST /api/v1/admin/pacs-instances/{pacsInstanceId}/connector/revoke</c>; audit <c>connector.revoked</c>.</summary>
public sealed class RevokeConnectorHandler(
    ITenantDb db,
    ITenantContext tenant,
    IConnectorRegistrar registrar,
    PacsAdminAudit audit,
    TimeProvider time)
{
    public async Task<Result> HandleAsync(RevokeConnectorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = command.PacsInstanceId;
        var pacsId = new PacsInstanceId(id);
        if (!await db.PacsInstances.AnyAsync(p => p.Id == pacsId, ct))
        {
            return PacsAdminErrors.NotFound;
        }

        var revoked = await registrar.RevokeAsync(tenant.TenantId, pacsId, time.GetUtcNow(), ct);
        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        await audit.RecordAsync("connector.revoked", "Connector", revoked.Value, new { pacsInstanceId = id }, ct);
        await db.SaveChangesAsync(ct);
        return Result.Success();
    }
}
