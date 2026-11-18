using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Identity;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

public sealed class LinkedPersonHandler(ICurrentUser currentUser) : AuthorizationHandler<LinkedPersonRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, LinkedPersonRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (currentUser.PersonId is not null)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
