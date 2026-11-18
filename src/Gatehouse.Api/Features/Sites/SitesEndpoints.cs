using System.Globalization;
using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Sites.GetArea;
using Gatehouse.Application.Sites.GetFloorBackground;
using Gatehouse.Application.Sites.ListSites;

namespace Gatehouse.Api.Features.Sites;

public static class SitesEndpoints
{
    public static IEndpointRouteBuilder MapSitesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/sites", async (ListSitesHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ListSitesQuery(), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ListSites");

        endpoints.MapGet("/api/v1/areas/{areaId:guid}", async (Guid areaId, GetAreaHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetAreaQuery(areaId), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetArea");

        // Same visibility as the access map; the ETag follows background_updated_at.
        endpoints.MapGet("/api/v1/floors/{floorId:guid}/background",
                async (Guid floorId, GetFloorBackgroundHandler handler, HttpContext http, CancellationToken ct) =>
            {
                var background = await handler.HandleAsync(new GetFloorBackgroundQuery(floorId), ct);
                if (background.IsFailure)
                {
                    return background.Error.ToProblem();
                }

                var etag = "\"" + background.Value.UpdatedAt.UtcTicks.ToString(CultureInfo.InvariantCulture) + "\"";
                http.Response.Headers.CacheControl = "private, max-age=300";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ETag = etag;
                if (http.Request.Headers.IfNoneMatch.Contains(etag))
                {
                    return Results.StatusCode(StatusCodes.Status304NotModified);
                }

                return Results.File(background.Value.Content, background.Value.ContentType);
            })
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("GetFloorBackground");

        return endpoints;
    }
}
