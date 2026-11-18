using Microsoft.AspNetCore.Authorization;

namespace Gatehouse.Application.Platform;

public sealed class PlatformOperatorHandler(IPlatformOperatorResolver resolver) : AuthorizationHandler<PlatformOperatorRequirement>
{
    public const string NotFoundCode = "platform.operator_not_found";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformOperatorRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.User.Identity?.IsAuthenticated == true
            && context.User.Identity.AuthenticationType == PlatformAuthentication.Scheme
            && await resolver.ResolveAsync(context.User, CancellationToken.None))
        {
            context.Succeed(requirement);
        }
    }
}
