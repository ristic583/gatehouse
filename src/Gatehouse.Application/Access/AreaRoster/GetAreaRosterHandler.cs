using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.AreaRoster;

public sealed class GetAreaRosterHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    TimeProvider time)
{
    public async Task<Result<AreaRosterDto>> HandleAsync(GetAreaRosterQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var areaId = new AreaId(query.AreaId);
        var area = await db.Areas.AsNoTracking()
            .Where(a => a.Id == areaId)
            .Join(db.Floors, a => a.FloorId, f => f.Id, (a, f) => new { Area = a, FloorName = f.Name, f.SiteId })
            .Join(db.Sites, x => x.SiteId, s => s.Id, (x, s) => new { x.Area, x.FloorName, x.SiteId, SiteName = s.Name })
            .SingleOrDefaultAsync(ct);
        if (area is null)
        {
            return Error.NotFound("area.not_found", "Area not found.");
        }

        var ownerIds = area.Area.Owners.Select(o => o.PersonId).ToHashSet();
        var resource = new AreaResource(areaId, area.SiteId, ownerIds);
        if (!(await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new ViewAreaRosterRequirement())).Succeeded)
        {
            return Error.Forbidden("auth.forbidden", "You are not allowed to view this roster.");
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var rows = await db.Entitlements.AsNoTracking()
            .Where(e => e.AreaId == areaId)
            .Join(db.Persons, e => e.PersonId, p => p.Id, (e, p) => new { Entitlement = e, p.GivenName, p.FamilyName, p.Type })
            .Where(x => (x.Entitlement.Status == EntitlementStatus.Approved && x.Entitlement.EffectiveEnd > now)
                || x.Entitlement.Status == EntitlementStatus.Revoked)
            .OrderBy(x => x.GivenName).ThenBy(x => x.FamilyName)
            .ToListAsync(ct);

        var ids = rows.Select(r => r.Entitlement.Id).ToList();
        var fulfillments = (await db.Fulfillments.AsNoTracking()
                .Where(f => ids.Contains(f.EntitlementId))
                .Select(f => new { f.EntitlementId, f.Kind, f.Status })
                .ToListAsync(ct))
            .ToLookup(f => f.EntitlementId, f => (f.Kind, f.Status));

        var entries = new List<AreaRosterEntryDto>();
        foreach (var r in rows)
        {
            var list = fulfillments[r.Entitlement.Id].ToList();
            var revokeStatus = AccessRequestDtoBuilder.SummarizeFulfillment(list, FulfillmentKind.Revoke);
            if (r.Entitlement.Status == EntitlementStatus.Revoked
                && (revokeStatus is null || revokeStatus == nameof(FulfillmentStatus.Confirmed)))
            {
                continue;
            }

            entries.Add(new AreaRosterEntryDto(
                r.Entitlement.Id.Value,
                new PersonRefDto(r.Entitlement.PersonId.Value, r.GivenName + " " + r.FamilyName),
                r.Type.ToString(),
                r.Entitlement.RequestedStart,
                r.Entitlement.EffectiveEnd,
                r.Entitlement.Status.ToString(),
                AccessRequestDtoBuilder.SummarizeFulfillment(list, FulfillmentKind.Grant),
                revokeStatus,
                r.Entitlement.Revocation?.RevokedAt));
        }

        var staffInScope = currentUser.IsSecurityStaff
            && (currentUser.SecurityStaffSiteIds is not { } sites || sites.Contains(area.SiteId));
        return new AreaRosterDto(
            new AreaRefDto(areaId.Value, area.Area.Name, area.SiteName, area.FloorName),
            ownerIds.Contains(me),
            staffInScope,
            entries);
    }
}
