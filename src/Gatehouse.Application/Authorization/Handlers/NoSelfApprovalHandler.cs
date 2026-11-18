using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>Hard rule 1: the subject or requester never decides a step. Uses <c>Fail</c>.</summary>
public sealed class NoSelfApprovalHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        if (requirement is ApproveStepRequirement && context.Resource is ApprovalStepResource s
            && (s.Entitlement.Subject.PersonId == me || s.Entitlement.RequestedById == me))
        {
            context.Fail(new AuthorizationFailureReason(this, "approval.self_approval_forbidden"));
        }
    }
}
