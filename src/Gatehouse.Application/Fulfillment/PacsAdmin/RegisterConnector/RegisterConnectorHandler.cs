using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gatehouse.Application.Fulfillment.PacsAdmin.RegisterConnector;

/// <summary>
/// <c>POST /api/v1/admin/pacs-instances/{pacsInstanceId}/connector</c>: enrollment ticket for the tenant from
/// <see cref="ITenantContext.TenantId"/>; audit <c>connector.registered</c>.
/// </summary>
public sealed class RegisterConnectorHandler(
    ITenantDb db,
    ITenantContext tenant,
    IConnectorRegistrar registrar,
    PacsAdminAudit audit,
    IConfiguration configuration)
{
    /// <summary>
    /// Published connector agent image shown in the install snippet (M11-21), e.g. <c>ghcr.io/owner/gatehouse-agent:1.0</c>.
    /// </summary>
    public const string AgentImageSetting = "Gateway:AgentImage";

    public async Task<Result<ConnectorEnrollmentDto>> HandleAsync(RegisterConnectorCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var id = command.PacsInstanceId;
        var pacsId = new PacsInstanceId(id);
        var instance = await db.PacsInstances.AsNoTracking().SingleOrDefaultAsync(p => p.Id == pacsId, ct);
        if (instance is null)
        {
            return PacsAdminErrors.NotFound;
        }

        var ticket = await registrar.CreateForInstanceAsync(tenant.TenantId, pacsId, ct);
        if (ticket.IsFailure)
        {
            return ticket.Error;
        }

        await audit.RecordAsync("connector.registered", "Connector", ticket.Value.ConnectorId, new { pacsInstanceId = id }, ct);
        await db.SaveChangesAsync(ct);
        return new ConnectorEnrollmentDto(
            ticket.Value.ConnectorId, ticket.Value.EnrollmentToken, ticket.Value.ExpiresAt, command.GatewayUrl,
            await registrar.GetCaCertificatePemAsync(ct), instance.Vendor,
            configuration[AgentImageSetting] is { Length: > 0 } image ? image : null);
    }
}
