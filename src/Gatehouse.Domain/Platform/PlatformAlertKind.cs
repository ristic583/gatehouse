namespace Gatehouse.Domain.Platform;

public enum PlatformAlertKind
{
    ProvisioningFailed,
    ConnectorOffline,
    CertificateExpiring,
    EnrollmentTokenExpired,
    StatisticsStale,
    AdminNotActivated,
}
