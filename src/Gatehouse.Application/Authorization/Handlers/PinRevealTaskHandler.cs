using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>
/// Only the staff member who claimed an InProgress <c>PinReveal</c> task may reveal its PIN (authorization.md §4), and only while the
/// task is still within their current security-staff scope (a scope reduced after the claim no longer reveals: GH-LIVE-03).
/// </summary>
public sealed class PinRevealTaskHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        if (requirement is RevealPinRequirement
            && context.Resource is StaffTaskResource t
            && t.Kind == StaffTaskKind.PinReveal
            && t.AssignedToId == currentPersonId
            && t.Status == StaffTaskStatus.InProgress
            && CurrentUser.IsSecurityStaff
            && (
                t.SiteId is { } site
                    ? CurrentUser.SecurityStaffSiteIds is not { } sites || sites.Contains(site)
                    : CurrentUser.SecurityStaffSiteIds is null))
        {
            context.Succeed(requirement);
        }
    }
}
