using Gatehouse.Domain.Platform;

namespace Gatehouse.Application.Platform.Dtos;

public sealed record PlatformAlertDto(
    PlatformAlertKind Kind, AlertSeverity Severity, string TenantId, string Message, DateTimeOffset Since);
