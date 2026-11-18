using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

/// <summary>Catalog connector registrations for tenant-admin PACS management (callers pass <c>ITenantContext.TenantId</c>).</summary>
public interface IConnectorRegistrar
{
    /// <summary>Existing token format and hash, valid 24 h; sets the instance to <c>Connector</c> mode.
    /// <c>connector.already_exists</c>.</summary>
    public Task<Result<ConnectorTicket>> CreateForInstanceAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, CancellationToken ct);

    /// <summary>Registration <c>Revoked</c>, instance back to <c>Manual</c>; outstanding commands stay for staff retry.
    /// <c>connector.not_found</c>.</summary>
    public Task<Result<Guid>> RevokeAsync(TenantId tenantId, PacsInstanceId pacsInstanceId, DateTimeOffset now, CancellationToken ct);

    public Task<IReadOnlyList<ConnectorRegistrationInfo>> ListAsync(TenantId tenantId, CancellationToken ct);

    public Task<string> GetCaCertificatePemAsync(CancellationToken ct);
}
