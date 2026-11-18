using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Platform.Tenants;

/// <summary>Platform alerts computed on read (<c>domain-model.md</c> §1 "Platform alerts").</summary>
public static class PlatformAlertRules
{
    public static readonly TimeSpan CertificateWarning = TimeSpan.FromDays(14);
    public static readonly TimeSpan StatisticsStaleAfter = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan AdminActivationGrace = TimeSpan.FromHours(72);

    public static IReadOnlyList<PlatformAlertDto> Compute(
        Tenant tenant,
        TenantProvisioning? provisioning,
        TenantStatistics? statistics,
        IEnumerable<ConnectorRegistration> registrations,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        ArgumentNullException.ThrowIfNull(registrations);
        var id = tenant.Id.Value;
        var alerts = new List<PlatformAlertDto>();

        if (provisioning is { State: ProvisioningState.Failed })
        {
            alerts.Add(
                new(
                    PlatformAlertKind.ProvisioningFailed, AlertSeverity.Error, id, $"Provisioning failed at step {provisioning.Step}.",
                    provisioning.UpdatedAt));
        }

        if (statistics is { ConnectorsOffline: > 0 })
        {
            alerts.Add(
                new(
                    PlatformAlertKind.ConnectorOffline, AlertSeverity.Warning, id, $"{statistics.ConnectorsOffline} connector(s) offline.",
                    statistics.ComputedAt));
        }

        foreach (var r in registrations)
        {
            if (r.Status == ConnectorStatus.Active && r.CertificateNotAfter is { } notAfter && notAfter < now + CertificateWarning)
            {
                alerts.Add(
                    new(
                        PlatformAlertKind.CertificateExpiring, AlertSeverity.Warning, id,
                        $"Certificate of connector '{r.DisplayName}' expires {notAfter:yyyy-MM-dd}.", notAfter - CertificateWarning));
            }

            if (r.Status == ConnectorStatus.PendingEnrollment && r.EnrollmentTokenExpiresAt is { } expires && expires < now)
            {
                alerts.Add(
                    new(
                        PlatformAlertKind.EnrollmentTokenExpired, AlertSeverity.Info, id,
                        $"Enrollment token of connector '{r.DisplayName}' expired.", expires));
            }
        }

        if (tenant.Status == TenantStatus.Active)
        {
            var computed = statistics?.ComputedAt;
            var reference = computed ?? provisioning?.CompletedAt ?? tenant.CreatedAt;
            if (now - reference > StatisticsStaleAfter)
            {
                alerts.Add(new(PlatformAlertKind.StatisticsStale, AlertSeverity.Warning, id, "Tenant statistics are stale.", reference));
            }
        }

        if (provisioning is { AdminInvitedAt: { } invited, AdminActivatedAt: null } && now - invited > AdminActivationGrace)
        {
            alerts.Add(
                new(PlatformAlertKind.AdminNotActivated, AlertSeverity.Info, id, "The first tenant admin has not signed in yet.", invited));
        }

        return alerts;
    }
}
