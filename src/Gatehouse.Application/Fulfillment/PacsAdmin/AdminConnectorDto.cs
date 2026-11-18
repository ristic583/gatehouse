using Gatehouse.Domain.Connectors;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

public sealed record AdminConnectorDto(
    Guid ConnectorId,
    ConnectorStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? EnrollmentTokenExpiresAt,
    DateTimeOffset? CertificateNotAfter,
    DateTimeOffset? LastSeenAt);
