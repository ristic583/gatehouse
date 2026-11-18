using System.Threading.RateLimiting;
using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Platform;
using Gatehouse.Application.Platform.CreateTenant;
using Gatehouse.Application.Platform.Dtos;
using Gatehouse.Application.Platform.GetTenant;
using Gatehouse.Application.Platform.ListPlatformAlerts;
using Gatehouse.Application.Platform.ListPlatformAudit;
using Gatehouse.Application.Platform.ListTenants;
using Gatehouse.Application.Platform.ReactivateTenant;
using Gatehouse.Application.Platform.ResendAdminInvite;
using Gatehouse.Application.Platform.RetryProvisioning;
using Gatehouse.Application.Platform.SuspendTenant;

namespace Gatehouse.Api.Features.Platform;

/// <summary>Platform operator routes: <c>PlatformBearer</c> only, catalog only (authorization.md §11).</summary>
public static class PlatformEndpoints
{
    /// <summary>10 tenant creations per minute per operator (M10-05 rule 4).</summary>
    private static readonly PartitionedRateLimiter<string> CreateTenantLimiter = PartitionedRateLimiter.Create<string, string>(
        key => RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
        }));

    public static IEndpointRouteBuilder MapPlatformEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup(PlatformAuthentication.RoutePrefix)
            .RequireAuthorization(Policies.PlatformOperator);

        group.MapGet("/me", GetMe).WithName("GetPlatformMe");
        group.MapGet("/tenants", async (ListTenantsHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)))
            .WithName("ListPlatformTenants");
        group.MapPost("/tenants", CreateTenantAsync).WithName("CreatePlatformTenant");
        group.MapGet("/tenants/{tenantId}", async (string tenantId, GetTenantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantId, ct)).ToHttpResult(Results.Ok)).WithName("GetPlatformTenant");
        group.MapPost("/tenants/{tenantId}/suspend", SuspendAsync).WithName("SuspendPlatformTenant");
        group.MapPost("/tenants/{tenantId}/reactivate", async (string tenantId, ReactivateTenantHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantId, ct)).ToHttpResult(Results.Ok)).WithName("ReactivatePlatformTenant");
        group.MapPost("/tenants/{tenantId}/provisioning/retry", async (
            string tenantId, RetryProvisioningHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantId, ct)).ToHttpResult(Results.Ok)).WithName("RetryPlatformTenantProvisioning");
        group.MapPost("/tenants/{tenantId}/admin-invite", async (string tenantId, ResendAdminInviteHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(tenantId, ct)).ToHttpResult()).WithName("ResendPlatformAdminInvite");
        group.MapGet(
            "/alerts", async (ListPlatformAlertsHandler handler, CancellationToken ct) => Results.Ok(await handler.HandleAsync(ct)))
            .WithName("ListPlatformAlerts");
        group.MapGet("/audit", async (string? tenantId, long? before, int? limit, ListPlatformAuditHandler handler, CancellationToken ct) =>
            Results.Ok(await handler.HandleAsync(new ListPlatformAuditQuery(tenantId, before, limit), ct))).WithName("ListPlatformAudit");

        return endpoints;
    }

    private static Microsoft.AspNetCore.Http.HttpResults.Ok<PlatformOperatorDto> GetMe(IPlatformOperator platformOperator) =>
        TypedResults.Ok(new PlatformOperatorDto(platformOperator.Id.Value, platformOperator.Email, platformOperator.DisplayName));

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantRequest request,
        IPlatformOperator platformOperator,
        CreateTenantHandler handler,
        CancellationToken ct)
    {
        using var lease = await CreateTenantLimiter.AcquireAsync(platformOperator.Id.Value.ToString(), 1, ct);
        if (!lease.IsAcquired)
        {
            return Results.StatusCode(StatusCodes.Status429TooManyRequests);
        }

        return (
            await handler.HandleAsync(request, ct)).ToHttpResult(dto =>
                Results.Accepted($"{PlatformAuthentication.RoutePrefix}/tenants/{dto.TenantId}", dto));
    }

    private static async Task<IResult> SuspendAsync(
        string tenantId,
        SuspendTenantRequest request,
        SuspendTenantHandler handler,
        CancellationToken ct)
    {
        return (await handler.HandleAsync(tenantId, request, ct)).ToHttpResult(Results.Ok);
    }
}
