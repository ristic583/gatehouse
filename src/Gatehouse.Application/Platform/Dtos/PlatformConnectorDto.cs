using Gatehouse.Domain.Connectors;

namespace Gatehouse.Application.Platform.Dtos;

public sealed record PlatformConnectorDto(
    Guid ConnectorId,
    string DisplayName,
    ConnectorStatus Status,
    DateTimeOffset? LastSeenAt,
    DateTimeOffset? CertificateNotAfter);
