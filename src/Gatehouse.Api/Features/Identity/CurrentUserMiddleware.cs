using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.Api.Features.Identity;

/// <summary>Loads <see cref="ICurrentUser"/> (person link + role assignments) once per request, after tenant resolution.</summary>
public sealed class CurrentUserMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, ITenantContext tenantContext, CurrentUserAccessor currentUser)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.User.Identity?.IsAuthenticated == true && tenantContext.IsResolved)
        {
            await currentUser.LoadAsync(
                context.User.FindFirst("sub")?.Value ?? string.Empty,
                context.User.FindFirst("email")?.Value,
                context.RequestAborted);
        }

        await next(context);
    }
}
