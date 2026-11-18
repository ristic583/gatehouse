using Gatehouse.Domain.Connectors;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>The non-revoked catalog registration of one PACS instance of the current tenant.</summary>
public sealed record ConnectorRegistrationInfo(
    Guid ConnectorId,
    Guid PacsInstanceId,
    ConnectorStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnrollmentTokenExpiresAt,
    DateTimeOffset? CertificateNotAfter,
    DateTimeOffset? LastSeenAt);
