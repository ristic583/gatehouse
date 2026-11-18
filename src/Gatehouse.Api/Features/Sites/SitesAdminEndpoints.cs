using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Sites.Admin;
using Gatehouse.Application.Sites.Background;
using Gatehouse.Application.Sites.CreateArea;
using Gatehouse.Application.Sites.CreateDoor;
using Gatehouse.Application.Sites.CreateFloor;
using Gatehouse.Application.Sites.CreateReader;
using Gatehouse.Application.Sites.CreateSite;
using Gatehouse.Application.Sites.DeleteArea;
using Gatehouse.Application.Sites.DeleteDoor;
using Gatehouse.Application.Sites.DeleteFloor;
using Gatehouse.Application.Sites.DeleteReader;
using Gatehouse.Application.Sites.DeleteSite;
using Gatehouse.Application.Sites.Dtos;
using Gatehouse.Application.Sites.ListSecurityLevels;
using Gatehouse.Application.Sites.RemoveFloorBackground;
using Gatehouse.Application.Sites.SetAreaOwners;
using Gatehouse.Application.Sites.UpdateArea;
using Gatehouse.Application.Sites.UpdateDoor;
using Gatehouse.Application.Sites.UpdateFloor;
using Gatehouse.Application.Sites.UpdateReader;
using Gatehouse.Application.Sites.UpdateSite;
using Gatehouse.Application.Sites.UploadFloorBackground;

namespace Gatehouse.Api.Features.Sites;

/// <summary>Tenant admin: sites, floors and the read-only security levels (authorization.md §11).</summary>
public static class SitesAdminEndpoints
{
    /// <summary>10 MiB drawing plus multipart overhead.</summary>
    public const long BackgroundUploadLimitBytes = FloorBackgroundLimits.MaxBytes + (256 * 1024);

    public static IEndpointRouteBuilder MapSitesAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin").RequireAuthorization(Policies.TenantAdmin);

        admin.MapGet("/security-levels", async (ListSecurityLevelsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListSecurityLevelsQuery(), ct)).ToHttpResult(Results.Ok))
            .WithName("ListAdminSecurityLevels");

        MapSites(admin);
        MapFloors(admin);
        MapAreas(admin);
        MapDoors(admin);
        MapBackgrounds(admin);
        return endpoints;
    }

    private static void MapSites(RouteGroupBuilder admin)
    {
        admin.MapPost("/sites", async (SaveSiteRequest request, CreateSiteHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CreateSiteCommand(request), ct))
                .ToHttpResult(dto => Results.Created($"/api/v1/admin/sites/{dto.SiteId}", dto)));
        admin.MapPut("/sites/{siteId:guid}",
            async (Guid siteId, SaveSiteRequest request, UpdateSiteHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateSiteCommand(siteId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/sites/{siteId:guid}", async (Guid siteId, DeleteSiteHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteSiteCommand(siteId), ct)).ToHttpResult());
    }

    private static void MapFloors(RouteGroupBuilder admin)
    {
        admin.MapPost("/sites/{siteId:guid}/floors",
            async (Guid siteId, SaveFloorRequest request, CreateFloorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateFloorCommand(siteId, request), ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/admin/floors/{dto.FloorId}", dto)));
        admin.MapPut("/floors/{floorId:guid}",
            async (Guid floorId, SaveFloorRequest request, UpdateFloorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateFloorCommand(floorId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/floors/{floorId:guid}", async (Guid floorId, DeleteFloorHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteFloorCommand(floorId), ct)).ToHttpResult());

        admin.MapGet("/floors/{floorId:guid}/plan", async (Guid floorId, FloorPlanReader plans, CancellationToken ct) =>
            (await plans.GetAsync(floorId, ct)).ToHttpResult(Results.Ok)).WithName("GetAdminFloorPlan");
    }

    private static void MapAreas(RouteGroupBuilder admin)
    {
        admin.MapPost("/floors/{floorId:guid}/areas",
            async (Guid floorId, SaveAreaRequest request, CreateAreaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateAreaCommand(floorId, request), ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/admin/areas/{dto.Id}", dto)));
        admin.MapPut("/areas/{areaId:guid}",
            async (Guid areaId, SaveAreaRequest request, UpdateAreaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateAreaCommand(areaId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/areas/{areaId:guid}", async (Guid areaId, DeleteAreaHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteAreaCommand(areaId), ct)).ToHttpResult());
        admin.MapPut("/areas/{areaId:guid}/owners",
            async (Guid areaId, SetAreaOwnersRequest request, SetAreaOwnersHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new SetAreaOwnersCommand(areaId, request), ct)).ToHttpResult(Results.Ok));
    }

    private static void MapDoors(RouteGroupBuilder admin)
    {
        admin.MapPost("/areas/{areaId:guid}/doors",
            async (Guid areaId, SaveDoorRequest request, CreateDoorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateDoorCommand(areaId, request), ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/admin/doors/{dto.Id}", dto)));
        admin.MapPut("/doors/{doorId:guid}",
            async (Guid doorId, SaveDoorRequest request, UpdateDoorHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateDoorCommand(doorId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/doors/{doorId:guid}", async (Guid doorId, DeleteDoorHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteDoorCommand(doorId), ct)).ToHttpResult());

        admin.MapPost("/doors/{doorId:guid}/readers",
            async (Guid doorId, SaveReaderRequest request, CreateReaderHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new CreateReaderCommand(doorId, request), ct))
                    .ToHttpResult(dto => Results.Created($"/api/v1/admin/readers/{dto.Id}", dto)));
        admin.MapPut("/readers/{readerId:guid}",
            async (Guid readerId, SaveReaderRequest request, UpdateReaderHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new UpdateReaderCommand(readerId, request), ct)).ToHttpResult(Results.Ok));
        admin.MapDelete("/readers/{readerId:guid}", async (Guid readerId, DeleteReaderHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new DeleteReaderCommand(readerId), ct)).ToHttpResult());
    }

    private static void MapBackgrounds(RouteGroupBuilder admin)
    {
        admin.MapPut("/floors/{floorId:guid}/background",
                async (Guid floorId, HttpRequest request, UploadFloorBackgroundHandler handler, CancellationToken ct) =>
                {
                    var upload = await ReadUploadAsync(request, ct);
                    if (upload is null)
                    {
                        return TooLarge("The drawing may be at most 10 MiB.");
                    }

                    var result = await handler.HandleAsync(new UploadFloorBackgroundCommand(floorId, upload), ct);
                    return result.IsFailure && result.Error.Code == "floor.background_too_large"
                        ? TooLarge(result.Error.Message)
                        : result.ToHttpResult();
                })
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(BackgroundUploadLimitBytes));
        admin.MapDelete("/floors/{floorId:guid}/background",
            async (Guid floorId, RemoveFloorBackgroundHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new RemoveFloorBackgroundCommand(floorId), ct)).ToHttpResult());
    }

    private static IResult TooLarge(string title) => Results.Problem(
        statusCode: StatusCodes.Status413PayloadTooLarge,
        title: title,
        extensions: new Dictionary<string, object?> { ["code"] = "floor.background_too_large" });

    /// <summary>The multipart <c>file</c> bytes; empty when missing (the processor rejects it), null when over 10 MiB.</summary>
    private static async Task<byte[]?> ReadUploadAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType)
        {
            return [];
        }

        var form = await request.ReadFormAsync(ct);
        var file = form.Files.GetFile("file");
        if (file is null)
        {
            return [];
        }

        if (file.Length > FloorBackgroundLimits.MaxBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
