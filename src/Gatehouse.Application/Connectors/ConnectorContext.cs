using System.Security.Cryptography.X509Certificates;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;

namespace Gatehouse.Application.Connectors;

/// <summary>The authenticated connector of the current gateway request; set by the certificate authentication handler.</summary>
public sealed class ConnectorContext
{
    private (TenantId Tenant, PacsInstanceId Pacs, ConnectorId Connector)? _value;

    public TenantId TenantId => Required().Tenant;

    public PacsInstanceId PacsInstanceId => Required().Pacs;

    public ConnectorId ConnectorId => Required().Connector;

    public bool IsResolved => _value is not null;

    /// <summary>The client certificate of the current request; set by the poll endpoint so <c>SetPin</c> can be encrypted to it.</summary>
    public X509Certificate2? ClientCertificate { get; set; }

    public void Set(TenantId tenantId, PacsInstanceId pacsInstanceId, ConnectorId connectorId) =>
        _value = (tenantId, pacsInstanceId, connectorId);

    private (TenantId Tenant, PacsInstanceId Pacs, ConnectorId Connector) Required() =>
        _value ?? throw new InvalidOperationException("No connector is authenticated for this request.");
}
