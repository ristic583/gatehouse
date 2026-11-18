using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.GetAccessRequest;

/// <summary>Builds the authorization resource for an entitlement (subject, requester, area owners, site).</summary>
public sealed class EntitlementResourceLoader(ITenantDb db)
{
    public async Task<EntitlementResource> LoadAsync(Entitlement entitlement, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entitlement);
        var subject = await db.Persons.AsNoTracking()
            .Where(p => p.Id == entitlement.PersonId)
            .Select(p => new PersonResource(p.Id, p.Type, p.ManagerId, p.SponsorId))
            .SingleAsync(ct);
        var area = await db.Areas.AsNoTracking().SingleAsync(a => a.Id == entitlement.AreaId, ct);
        var siteId = await db.Floors.Where(f => f.Id == area.FloorId).Select(f => f.SiteId).SingleAsync(ct);
        return new EntitlementResource(
            entitlement.Id,
            subject,
            entitlement.RequestedById,
            new AreaResource(area.Id, siteId, area.Owners.Select(o => o.PersonId).ToHashSet()));
    }
}
