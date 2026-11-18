using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Credentials.ActivateCard;
using Gatehouse.Application.Credentials.AssignCard;
using Gatehouse.Application.Credentials.DeactivateCard;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Credentials.Photos.DeleteMyPhoto;
using Gatehouse.Application.Credentials.Photos.GetPersonPhoto;
using Gatehouse.Application.Credentials.Photos.UploadMyPhoto;
using Gatehouse.Application.Credentials.RequestPinReveal;
using Gatehouse.Application.Credentials.SetPin;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;

namespace Gatehouse.Api.Features.Credentials;

public static class CredentialsEndpoints
{
    public static IEndpointRouteBuilder MapCredentialsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me/cards", async (ListMyCardsHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ListMyCards");

        endpoints.MapGet("/api/v1/people/{personId:guid}/cards",
                async (Guid personId, ListPersonCardsHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(new ListPersonCardsQuery(new PersonId(personId)), ct)).ToHttpResult(Results.Ok))
            .RequireAuthorization(Policies.LinkedPerson)
            .WithName("ListPersonCards");

        endpoints.MapPost("/api/v1/people/{personId:guid}/cards",
                async (Guid personId, AssignCardRequest request, AssignCardHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(new AssignCardCommand(new PersonId(personId), request.CardNumber), ct))
                        .ToHttpResult(dto => Results.Created($"/api/v1/people/{personId}/cards", dto)))
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPost("/api/v1/cards/{cardId:guid}/activate", async (Guid cardId, ActivateCardHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new ActivateCardCommand(new CredentialId(cardId)), ct))
                    .ToHttpResult(dto => Results.Accepted(null, dto)))
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPost("/api/v1/cards/{cardId:guid}/deactivate",
                async (Guid cardId, DeactivateCardRequest request, DeactivateCardHandler handler, CancellationToken ct) =>
                    (await handler.HandleAsync(new DeactivateCardCommand(new CredentialId(cardId), request.Reason), ct))
                        .ToHttpResult(dto => Results.Accepted(null, dto)))
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPut("/api/v1/me/pin", async (SetPinRequest request, SetPinHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request, ct)).ToHttpResult())
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPut("/api/v1/me/photo", async (HttpRequest request, UploadMyPhotoHandler handler, CancellationToken ct) =>
            {
                var bytes = await ReadUploadAsync(request, ct);
                return bytes is null
                    ? Gatehouse.Domain.Common.Error.Validation("photo.too_large", "A photo may be at most 2 MiB.").ToProblem()
                    : (await handler.HandleAsync(new UploadMyPhotoCommand(bytes), ct)).ToHttpResult();
            })
            .RequireAuthorization(Policies.LinkedPerson)
            .DisableAntiforgery()
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(UploadLimitBytes));

        endpoints.MapDelete("/api/v1/me/photo", async (DeleteMyPhotoHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new DeleteMyPhotoCommand(), ct)).ToHttpResult())
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapGet("/api/v1/people/{personId:guid}/photo",
                async (Guid personId, GetPersonPhotoHandler handler, HttpContext http, CancellationToken ct) =>
            {
                var photo = await handler.HandleAsync(new GetPersonPhotoQuery(new PersonId(personId)), ct);
                if (photo.IsFailure)
                {
                    return photo.Error.ToProblem();
                }

                http.Response.Headers.CacheControl = "private, no-store";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                return Results.File(photo.Value.Content, photo.Value.ContentType);
            })
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPost("/api/v1/me/pin/reveal-requests", async (RequestPinRevealHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(ct)).ToHttpResult(dto => Results.Accepted(null, dto)))
            .RequireAuthorization(Policies.LinkedPerson);
        return endpoints;
    }

    /// <summary>2 MiB photo plus multipart overhead.</summary>
    public const long UploadLimitBytes = Gatehouse.Domain.Credentials.Photo.MaxSizeBytes + (64 * 1024);

    /// <summary>The <c>file</c> part of a multipart upload, or null when it is larger than 2 MiB.</summary>
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

        if (file.Length > Gatehouse.Domain.Credentials.Photo.MaxSizeBytes)
        {
            return null;
        }

        using var buffer = new MemoryStream();
        await file.CopyToAsync(buffer, ct);
        return buffer.ToArray();
    }
}
