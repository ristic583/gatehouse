using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

public sealed class TeamManagerHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        var ok = (requirement, context.Resource) switch
        {
            (RequestAccessRequirement or ActivateCardRequirement or ManagePersonRequirement, PersonResource p) => p.ManagerId == me,
            (ApproveStepRequirement, ApprovalStepResource s) => s.ApproverKind == ApproverKind.Manager && s.AssignedApproverId == me,
            (ViewEntitlementRequirement, EntitlementResource e) => e.Subject.ManagerId == me,
            _ => false,
        };
        if (ok)
        {
            context.Succeed(requirement);
        }
    }
}
