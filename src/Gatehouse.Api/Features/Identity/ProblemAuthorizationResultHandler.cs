using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Platform;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace Gatehouse.Api.Features.Identity;

/// <summary>Turns policy failures into problem details with a code (authorization.md §4).</summary>
public sealed class ProblemAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden && authorizeResult.AuthorizationFailure is { } failure)
        {
            var error = failure.FailedRequirements.Any(r => r is LinkedPersonRequirement)
                ? Error.Forbidden(PersonNotProvisionedException.Code, "The signed-in user has no active person in this tenant.")
                : failure.FailedRequirements.Any(r => r is PlatformOperatorRequirement)
                    ? Error.Forbidden(PlatformOperatorHandler.NotFoundCode, "The signed-in user is not an active platform operator.")
                    : Error.Forbidden("auth.forbidden", "You are not allowed to do this.");
            await error.ToProblem().ExecuteAsync(context);
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}
