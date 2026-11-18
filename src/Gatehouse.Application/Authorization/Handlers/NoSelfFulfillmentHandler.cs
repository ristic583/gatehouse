using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>Hard rule 4: staff never handle a fulfillment of their own request. Uses <c>Fail</c>.</summary>
public sealed class NoSelfFulfillmentHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        var me = currentPersonId;
        if (requirement is HandleFulfillmentRequirement && context.Resource is FulfillmentResource f
            && (f.SubjectId == me || f.RequestedById == me))
        {
            context.Fail(new AuthorizationFailureReason(this, "fulfillment.self_handling_forbidden"));
        }
    }
}
