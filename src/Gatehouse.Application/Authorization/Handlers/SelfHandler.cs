using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

public sealed class SelfHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        var ok = (requirement, context.Resource) switch
        {
            (RequestAccessRequirement, PersonResource p) => p.PersonId == me,
            (ViewEntitlementRequirement, EntitlementResource e) => e.Subject.PersonId == me || e.RequestedById == me,
            _ => false,
        };
        if (ok)
        {
            context.Succeed(requirement);
        }
    }
}
