using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Fulfillment.PacsAdmin.CreatePacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.DeletePacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.GetPacsInstance;
using Gatehouse.Application.Fulfillment.PacsAdmin.ListPacsInstances;
using Gatehouse.Application.Fulfillment.PacsAdmin.RegisterConnector;
using Gatehouse.Application.Fulfillment.PacsAdmin.RevokeConnector;
using Gatehouse.Application.Fulfillment.PacsAdmin.SetAccessLevelMappings;
using Gatehouse.Application.Fulfillment.PacsAdmin.UpdatePacsInstance;

namespace Gatehouse.Api.Features.Fulfillment;

/// <summary>Tenant admin: PACS instances, access-level mappings, connector registration (authorization.md §11).</summary>
public static class PacsAdminEndpoints
{
    /// <summary>Shown in the connector install snippet when <c>Gateway:PublicUrl</c> is not configured (AppHost gateway port).</summary>
    public const string DefaultGatewayUrl = "https://localhost:5443";

    public static IEndpointRouteBuilder MapPacsAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/pacs-instances").RequireAuthorization(Policies.TenantAdmin);

        admin.MapGet(string.Empty, async (ListPacsInstancesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListPacsInstancesQuery(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListPacsInstancesAdmin");
        admin.MapPost(string.Empty, async (SavePacsInstanceRequest request, CreatePacsInstanceHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CreatePacsInstanceCommand(request), ct))
                .ToHttpResult(dto => Results.Created($"/api/v1/admin/pacs-instances/{dto.Id}", dto)));
        admin.MapGet("/{pacsInstanceId:guid}", async (Guid pacsInstanceId, GetPacsInstanceHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new GetPacsInstanceQuery(pacsInstanceId), ct)).ToHttpResult(Results.Ok));
        admin.MapPut("/{pacsInstanceId:guid}",
            async (Guid pacsInstanceId, SavePacsInstanceRequest request, UpdatePacsInstanceHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdatePacsInstanceCommand(pacsInstanceId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/{pacsInstanceId:guid}", async (Guid pacsInstanceId, DeletePacsInstanceHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeletePacsInstanceCommand(pacsInstanceId), ct)).ToHttpResult());
        admin.MapPut("/{pacsInstanceId:guid}/mappings",
            async (Guid pacsInstanceId, List<AccessLevelMappingRequest> request, SetAccessLevelMappingsHandler handler,
                CancellationToken ct) =>
                (await handler.HandleAsync(new SetAccessLevelMappingsCommand(pacsInstanceId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapPost("/{pacsInstanceId:guid}/connector",
            async (Guid pacsInstanceId, RegisterConnectorHandler handler, IConfiguration configuration, HttpContext http,
                CancellationToken ct) =>
            {
                var gatewayUrl = configuration["Gateway:PublicUrl"] is { Length: > 0 } url ? url : DefaultGatewayUrl;
                var result = await handler.HandleAsync(new RegisterConnectorCommand(pacsInstanceId, gatewayUrl), ct);
                http.Response.Headers.CacheControl = "no-store";
                return result.ToHttpResult(dto => Results.Created($"/api/v1/admin/pacs-instances/{pacsInstanceId}", dto));
            });
        admin.MapPost("/{pacsInstanceId:guid}/connector/revoke",
            async (Guid pacsInstanceId, RevokeConnectorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RevokeConnectorCommand(pacsInstanceId), ct)).ToHttpResult());

        return endpoints;
    }
}
