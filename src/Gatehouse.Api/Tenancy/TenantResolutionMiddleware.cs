using Gatehouse.Api.Http;
using Gatehouse.Application.Platform;
using Gatehouse.Domain.Common;
using Gatehouse.Infrastructure.Tenancy;
using Serilog.Context;

namespace Gatehouse.Api.Tenancy;

/// <summary>
/// Resolves the tenant of an authenticated user from the <c>tenant_id</c> claim only (ADR-001).
/// Runs after authentication and before authorization.
/// </summary>
public sealed class TenantResolutionMiddleware(RequestDelegate next)
{
    public const string TenantClaim = "tenant_id";

    public async Task InvokeAsync(
        HttpContext context,
        ITenantConnectionResolver resolver,
        ITenantContextSetter tenantContext)
    {
        ArgumentNullException.ThrowIfNull(context);

        // Platform routes are catalog-only and authenticate with PlatformBearer (ADR-009 §2): never resolve a tenant there,
        // and never treat a PlatformBearer principal as a tenant user.
        if (context.User.Identity?.IsAuthenticated != true
            || context.User.Identity.AuthenticationType == PlatformAuthentication.Scheme
            || context.Request.Path.StartsWithSegments(PlatformAuthentication.RoutePrefix, StringComparison.OrdinalIgnoreCase))
        {
            await next(context);
            return;
        }

        var claim = context.User.FindFirst(TenantClaim)?.Value;
        if (string.IsNullOrEmpty(claim))
        {
            await Error.Forbidden("tenant.missing_claim", "The token has no tenant_id claim.").ToProblem().ExecuteAsync(context);
            return;
        }

        var tenantId = TenantId.Create(claim);
        if (tenantId.IsFailure)
        {
            await Error.Forbidden("tenant.unknown", "The tenant is not known.").ToProblem().ExecuteAsync(context);
            return;
        }

        var connection = await resolver.ResolveAsync(tenantId.Value, context.RequestAborted);
        if (connection.IsFailure)
        {
            await connection.Error.ToProblem().ExecuteAsync(context);
            return;
        }

        tenantContext.Set(tenantId.Value, connection.Value);

        using (LogContext.PushProperty("TenantId", tenantId.Value.Value))
        {
            await next(context);
        }
    }
}
