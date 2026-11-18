using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Tenancy;
using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Authorization.Handlers;

public sealed class TenantMemberHandler(ITenantContext tenantContext) : AuthorizationHandler<TenantMemberRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, TenantMemberRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (tenantContext.IsResolved)
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}
