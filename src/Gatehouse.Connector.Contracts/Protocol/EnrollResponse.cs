namespace Gatehouse.Connector.Contracts.Protocol;

public sealed record EnrollResponse(
    Guid ConnectorId, string TenantId, Guid PacsInstanceId, string CertificatePem, string CaChainPem, DateTimeOffset NotAfter);
