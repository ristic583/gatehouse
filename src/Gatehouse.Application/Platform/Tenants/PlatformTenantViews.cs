using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Platform;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Platform.Tenants;

/// <summary>Builds platform tenant DTOs from catalog rows only.</summary>
public sealed class PlatformTenantViews(IPlatformCatalogDb catalog, TimeProvider time)
{
    public async Task<IReadOnlyList<PlatformTenantSummaryDto>> ListAsync(CancellationToken ct)
    {
        var data = await LoadAsync(null, ct);
        return data.Tenants.OrderBy(t => t.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(t => Summary(t, data))
            .ToList();
    }

    public async Task<IReadOnlyList<PlatformAlertDto>> AlertsAsync(CancellationToken ct)
    {
        var data = await LoadAsync(null, ct);
        return data.Tenants.SelectMany(t => Alerts(t, data))
            .OrderByDescending(a => a.Severity).ThenBy(a => a.Since)
            .ToList();
    }

    public async Task<Result<PlatformTenantDto>> GetAsync(TenantId id, CancellationToken ct)
    {
        var data = await LoadAsync(id, ct);
        if (data.Tenants.Count == 0)
        {
            return PlatformErrors.TenantNotFound;
        }

        var tenant = data.Tenants[0];
        var s = Summary(tenant, data);
        var provisioning = data.Provisionings.GetValueOrDefault(tenant.Id);
        var statistics = data.Statistics.GetValueOrDefault(tenant.Id);
        var connectors = data.Registrations.Where(r => r.TenantId == tenant.Id)
            .OrderBy(r => r.CreatedAt)
            .Select(r => new PlatformConnectorDto(r.Id.Value, r.DisplayName, r.Status, r.LastSeenAt, r.CertificateNotAfter))
            .ToList();
        return new PlatformTenantDto(
            s.TenantId, s.DisplayName, s.Status, s.EmailDomain, s.IdpKind, s.ProvisioningStep, s.People, s.Sites, s.ConnectorsOnline,
            s.ConnectorsOffline, s.AlertCount, s.CreatedAt,
            provisioning is null ? null : new TenantProvisioningDto(
                provisioning.Step, provisioning.State, provisioning.Attempts, provisioning.LastError, provisioning.StartedAt,
                provisioning.CompletedAt, provisioning.AdminInvitedAt, provisioning.AdminActivatedAt),
            statistics is null ? null : new TenantStatisticsDto(
                statistics.People, statistics.Employees, statistics.TenantAdmins, statistics.Sites, statistics.Floors, statistics.Areas,
                statistics.Doors, statistics.PacsInstances, statistics.ConnectorsOnline, statistics.ConnectorsOffline,
                statistics.ConnectorsNeverSeen,
                statistics.ActiveEntitlements, statistics.PendingRequests, statistics.OpenAlarms, statistics.UnconfirmedFulfillments,
                statistics.LinkedPeople, statistics.OnboardingCompletedAt, statistics.ComputedAt),
            connectors,
            Alerts(tenant, data),
            tenant.SuspendedAt,
            tenant.SuspendedReason);
    }

    private PlatformTenantSummaryDto Summary(Tenant t, CatalogData data)
    {
        var p = data.Provisionings.GetValueOrDefault(t.Id);
        var s = data.Statistics.GetValueOrDefault(t.Id);
        return new PlatformTenantSummaryDto(
            t.Id.Value, t.DisplayName, t.Status, t.EmailDomain, t.IdpKind,
            p is null || p.State == ProvisioningState.Completed ? null : p.Step,
            s?.People ?? 0, s?.Sites ?? 0, s?.ConnectorsOnline ?? 0, s?.ConnectorsOffline ?? 0,
            Alerts(t, data).Count, t.CreatedAt);
    }

    private IReadOnlyList<PlatformAlertDto> Alerts(Tenant t, CatalogData data) => PlatformAlertRules.Compute(
        t, data.Provisionings.GetValueOrDefault(t.Id), data.Statistics.GetValueOrDefault(t.Id),
        data.Registrations.Where(r => r.TenantId == t.Id), time.GetUtcNow());

    private async Task<CatalogData> LoadAsync(TenantId? id, CancellationToken ct)
    {
        var tenants = catalog.Tenants.AsNoTracking();
        var provisionings = catalog.TenantProvisionings.AsNoTracking();
        var statistics = catalog.TenantStatistics.AsNoTracking();
        var registrations = catalog.ConnectorRegistrations.AsNoTracking().Where(r => r.Status != ConnectorStatus.Revoked);
        if (id is { } tenantId)
        {
            tenants = tenants.Where(t => t.Id == tenantId);
            provisionings = provisionings.Where(p => p.TenantId == tenantId);
            statistics = statistics.Where(s => s.TenantId == tenantId);
            registrations = registrations.Where(r => r.TenantId == tenantId);
        }

        return new CatalogData(
            await tenants.ToListAsync(ct),
            (await provisionings.ToListAsync(ct)).ToDictionary(p => p.TenantId),
            (await statistics.ToListAsync(ct)).ToDictionary(s => s.TenantId),
            await registrations.ToListAsync(ct));
    }

    private sealed record CatalogData(
        List<Tenant> Tenants,
        Dictionary<TenantId, TenantProvisioning> Provisionings,
        Dictionary<TenantId, TenantStatistics> Statistics,
        List<ConnectorRegistration> Registrations);
}
