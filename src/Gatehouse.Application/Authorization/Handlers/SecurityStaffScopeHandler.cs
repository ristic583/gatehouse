using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

public sealed class SecurityStaffScopeHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        if (!CurrentUser.IsSecurityStaff)
        {
            return;
        }

        var ok = (requirement, context.Resource) switch
        {
            (SecurityStaffRequirement, _) => true,
            (ViewEntitlementRequirement, EntitlementResource e) => InScope(e.Area.SiteId),
            (EmergencyRevokeRequirement, EntitlementResource e) => InScope(e.Area.SiteId),
            (HandleFulfillmentRequirement, FulfillmentResource f) => InScope(f.SiteId),
            (HandleStaffTaskRequirement, StaffTaskResource t) => t.SiteId is { } s ? InScope(s) : CurrentUser.SecurityStaffSiteIds is null,
            (ViewAreaRosterRequirement, AreaResource a) => InScope(a.SiteId),
            (StaffConsoleRequirement, _) => true,
            (ViewLiveSiteRequirement, SiteId s) => InScope(s),
            (OperateDoorsRequirement, SiteId s) => InScope(s),
            (AuditReaderRequirement, _) => CurrentUser.SecurityStaffSiteIds is null,
            (ViewReconciliationRequirement, PacsInstanceResource p) => CurrentUser.SecurityStaffSiteIds is null || p.SiteIds.Any(InScope),
            (RunReconciliationRequirement, PacsInstanceResource p) =>
                CurrentUser.SecurityStaffSiteIds is null || (p.SiteIds.Count > 0 && p.SiteIds.All(InScope)),
            _ => false,
        };
        if (ok)
        {
            context.Succeed(requirement);
        }
    }

    private bool InScope(SiteId siteId) => CurrentUser.SecurityStaffSiteIds is not { } sites || sites.Contains(siteId);
}
