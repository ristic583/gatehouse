using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.LiveBuilding;
using Gatehouse.Application.LiveBuilding.ConnectorHealth;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>
/// <see cref="PacsInstanceAdminDto"/> with mappings, unmapped areas, registration and health. The tenant for catalog
/// registrations is always <see cref="ITenantContext.TenantId"/>.
/// </summary>
public sealed class PacsInstanceAdminReadModel(
    ITenantDb db,
    ITenantContext tenant,
    IConnectorRegistrar registrar,
    IConnectorHealthCache healthCache,
    TimeProvider time)
{
    public async Task<IReadOnlyList<ConnectorRegistrationInfo>> RegistrationsAsync(CancellationToken ct) =>
        await registrar.ListAsync(tenant.TenantId, ct);

    public async Task<PacsInstanceAdminDto> ToDtoAsync(
        PacsInstance instance, IReadOnlyList<ConnectorRegistrationInfo> registrations, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(instance);
        ArgumentNullException.ThrowIfNull(registrations);
        var mappings = await (from m in db.AccessLevelMappings.AsNoTracking()
                              join a in db.Areas.AsNoTracking() on m.AreaId equals a.Id
                              where m.PacsInstanceId == instance.Id
                              orderby a.Name
                              select new AccessLevelMappingDto(a.Id.Value, a.Name, m.VendorAccessLevelRef, m.DisplayName)).ToListAsync(ct);
        var siteIds = instance.SiteIds.ToList();
        var areasOnSites = await (from a in db.Areas.AsNoTracking()
                                  join f in db.Floors.AsNoTracking() on a.FloorId equals f.Id
                                  where siteIds.Contains(f.SiteId) && a.AccessControlled
                                  select a.Id.Value).ToListAsync(ct);
        var mapped = mappings.Select(m => m.AreaId).ToHashSet();
        var registration = registrations.FirstOrDefault(r => r.PacsInstanceId == instance.Id.Value);
        var healthList = await ConnectorHealthProjection.BuildAsync(db, healthCache, [instance], time.GetUtcNow(), ct);
        var health = healthList.Count > 0 ? healthList[0] : null;
        return new PacsInstanceAdminDto(
            instance.Id.Value, instance.Name, instance.Vendor, siteIds.Select(s => s.Value).ToList(), instance.FulfillmentMode, mappings,
            areasOnSites.Count(a => !mapped.Contains(a)),
            registration is null ? null : new AdminConnectorDto(registration.ConnectorId, registration.Status, registration.CreatedAt,
                registration.EnrollmentTokenExpiresAt, registration.CertificateNotAfter, registration.LastSeenAt),
            health);
    }
}
