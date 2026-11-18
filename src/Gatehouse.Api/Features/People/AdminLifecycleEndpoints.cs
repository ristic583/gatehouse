using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.People.Export;
using Gatehouse.Application.People.HrSync;
using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Application.People.Retention;

namespace Gatehouse.Api.Features.People;

public static class AdminLifecycleEndpoints
{
    public static IEndpointRouteBuilder MapAdminLifecycleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/lifecycle").RequireAuthorization(Policies.TenantAdmin);
        group.MapPost("/run", async (LifecycleAdminHandler handler, CancellationToken ct) => Results.Ok(await handler.RunAsync(ct)));
        group.MapGet("/settings",
            async (LifecycleAdminHandler handler, CancellationToken ct) => Results.Ok(await handler.GetSettingsAsync(ct)))
            .WithName("GetLifecycleSettings");
        group.MapPut("/settings", async (SetLifecycleSettingRequest request, LifecycleAdminHandler handler, CancellationToken ct) =>
            (await handler.SetAsync(request, ct)).ToHttpResult(Results.Ok));
        endpoints.MapPost("/api/v1/admin/hr-sync",
            async (HrSyncAdminHandler handler, CancellationToken ct) => (await handler.RunAsync(ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.TenantAdmin);
        var retention = endpoints.MapGroup("/api/v1/admin/retention").RequireAuthorization(Policies.TenantAdmin);
        retention.MapGet("/settings",
            async (RetentionAdminHandler handler, CancellationToken ct) => Results.Ok(await handler.GetAsync(ct)));
        retention.MapPut("/settings", async (SetRetentionSettingRequest request, RetentionAdminHandler handler, CancellationToken ct) =>
            (await handler.SetAsync(request, ct)).ToHttpResult(Results.Ok));
        retention.MapPost("/run", async (RetentionAdminHandler handler, CancellationToken ct) => Results.Ok(await handler.RunAsync(ct)));

        endpoints.MapGet("/api/v1/people/{personId:guid}/export",
            async (Guid personId, PersonExportHandler handler, HttpContext http, CancellationToken ct) =>
            {
                var export = await handler.HandleAsync(new Gatehouse.Domain.People.PersonId(personId), ct);
                if (export.IsFailure)
                {
                    return export.Error.ToProblem();
                }

                http.Response.Headers.ContentDisposition = $"attachment; filename=\"person-{personId}.json\"";
                http.Response.Headers.CacheControl = "no-store";
                return Results.Json(export.Value);
            })
            .RequireAuthorization(Policies.TenantAdmin);
        return endpoints;
    }
}
