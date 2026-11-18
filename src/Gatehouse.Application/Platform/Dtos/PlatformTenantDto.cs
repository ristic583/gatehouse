using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Platform.Dtos;

public sealed record PlatformTenantDto(
    string TenantId,
    string DisplayName,
    TenantStatus Status,
    string? EmailDomain,
    IdpKind IdpKind,
    ProvisioningStep? ProvisioningStep,
    int People,
    int Sites,
    int ConnectorsOnline,
    int ConnectorsOffline,
    int AlertCount,
    DateTimeOffset CreatedAt,
    TenantProvisioningDto? Provisioning,
    TenantStatisticsDto? Statistics,
    IReadOnlyList<PlatformConnectorDto> Connectors,
    IReadOnlyList<PlatformAlertDto> Alerts,
    DateTimeOffset? SuspendedAt,
    string? SuspendedReason);
