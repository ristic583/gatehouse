using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.People;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

/// <summary>Employees activate their own card; externals, visitors and guests do not (authorization.md §2, §4).</summary>
public sealed class SelfEmployeeHandler(ICurrentUser currentUser) : ResourceHandler(currentUser)
{
    protected override void Handle(AuthorizationHandlerContext context, IAuthorizationRequirement requirement, PersonId currentPersonId)
    {
        if (requirement is ActivateCardRequirement && context.Resource is PersonResource p && p.PersonId == currentPersonId
            && p.Type == PersonType.Employee)
        {
            context.Succeed(requirement);
        }
    }
}
