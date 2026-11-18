using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>Never handles <see cref="ApproveStepRequirement"/> (hard rule 2).</summary>
public sealed class TenantAdminHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        if (!CurrentUser.IsTenantAdmin)
        {
            return;
        }

        var ok = requirement switch
        {
            TenantAdminRequirement or ManageTenantRequirement or StaffConsoleRequirement or AuditReaderRequirement => true,
            ViewEntitlementRequirement => context.Resource is EntitlementResource,
            ManagePersonRequirement => context.Resource is PersonResource,
            ViewLiveSiteRequirement => context.Resource is Domain.Sites.SiteId,
            ViewReconciliationRequirement => context.Resource is PacsInstanceResource,
            _ => false,
        };
        if (ok)
        {
            context.Succeed(requirement);
        }
    }
}
