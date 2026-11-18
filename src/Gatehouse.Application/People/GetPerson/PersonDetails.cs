using System.Security.Claims;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.ListPeople;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.GetPerson;

/// <summary>Builds <see cref="PersonDetailDto"/>: summary, cards, the caller's rights and the entitlements the caller may see.</summary>
public sealed class PersonDetails(
    ITenantDb db,
    ICurrentUser currentUser,
    CardAccess access,
    CardReadModel cards,
    PersonSummaries summaries,
    IAuthorizationService authorization,
    EntitlementResourceLoader resources)
{
    public async Task<PersonDetailDto> BuildAsync(Person person, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(person);
        var resource = CardAccess.ResourceFor(person);
        var canManage = await access.AllowedAsync(resource, new ManagePersonRequirement());
        var canActivate = await access.AllowedAsync(resource, new ActivateCardRequirement());
        var personCards = await db.Cards.AsNoTracking()
            .Where(c => c.PersonId == person.Id)
            .OrderByDescending(c => c.StateChangedAt)
            .ToListAsync(ct);

        var entitlements = await db.Entitlements.AsNoTracking()
            .VisibleTo(currentUser, db.Areas, db.Persons, db.Floors)
            .Where(e => e.PersonId == person.Id)
            .OrderByDescending(e => e.CreatedAt)
            .Join(db.Areas, e => e.AreaId, a => a.Id, (e, a) => new { e.Id, AreaName = a.Name, e.Status, e.EffectiveEnd })
            .ToListAsync(ct);
        var ids = entitlements.Select(e => e.Id).ToList();
        var revokes = await db.Fulfillments.AsNoTracking()
            .Where(f => ids.Contains(f.EntitlementId) && f.Kind == FulfillmentKind.Revoke)
            .Select(f => new { f.EntitlementId, f.Status })
            .ToListAsync(ct);

        var revokeActions = await RevokeActionsAsync(
            entitlements.Where(e => e.Status == EntitlementStatus.Approved).Select(e => e.Id).ToList(), ct);
        var summary = (await summaries.ToDtosAsync([person], ct))[0];
        return new PersonDetailDto(
            summary,
            await cards.ToDtosAsync(personCards, ct),
            canManage,
            canActivate,
            canManage && person.Type == PersonType.Guest && person.Status == PersonStatus.Active,
            entitlements.Select(e => new PersonEntitlementDto(
                e.Id.Value,
                e.AreaName,
                e.Status.ToString(),
                e.EffectiveEnd,
                revokes.Where(r => r.EntitlementId == e.Id).Select(r => r.Status.ToString()).ToList(),
                revokeActions.GetValueOrDefault(e.Id))).ToList(),
            person.Status == PersonStatus.Active && await access.CanIssueAsync(resource),
            await access.CanDeactivateAsync(resource),
            currentUser.IsTenantAdmin && person.Type == PersonType.Employee && person.Status == PersonStatus.Active
                && currentUser.PersonId != person.Id);
    }

    /// <summary>
    /// Per approved entitlement, the revoke endpoint the caller may use (lab finding #5): <c>revoke</c> for an area owner,
    /// <c>emergency-revoke</c> for security staff in scope, else none.
    /// </summary>
    private async Task<Dictionary<EntitlementId, string>> RevokeActionsAsync(List<EntitlementId> approved, CancellationToken ct)
    {
        var result = new Dictionary<EntitlementId, string>();
        if (approved.Count == 0)
        {
            return result;
        }

        var principal = new ClaimsPrincipal();
        foreach (var entitlement in await db.Entitlements.AsNoTracking().Where(e => approved.Contains(e.Id)).ToListAsync(ct))
        {
            var resource = await resources.LoadAsync(entitlement, ct);
            if ((await authorization.AuthorizeAsync(principal, resource, new RevokeEntitlementRequirement())).Succeeded)
            {
                result[entitlement.Id] = "revoke";
            }
            else if ((await authorization.AuthorizeAsync(principal, resource, new EmergencyRevokeRequirement())).Succeeded)
            {
                result[entitlement.Id] = "emergency-revoke";
            }
        }

        return result;
    }
}
