using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Identity.MagicLinks;
using Gatehouse.Domain.People;

namespace Gatehouse.Api.Features.Identity;

public static class MagicLinkEndpoints
{
    public const string GuestSessionRateLimit = "guest-sessions";

    public static IEndpointRouteBuilder MapMagicLinkEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/v1/people/{personId:guid}/magic-links",
            async (Guid personId, IssueMagicLinkHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new PersonId(personId), ct)).ToHttpResult(dto => Results.Created((string?)null, dto)))
            .RequireAuthorization(Policies.LinkedPerson);

        endpoints.MapPost("/api/v1/guest/sessions",
            async (GuestSessionRequest? request, RedeemGuestSessionHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(request ?? new GuestSessionRequest(null), ct)).ToHttpResult(Results.Ok))
            .AllowAnonymous()
            .RequireRateLimiting(GuestSessionRateLimit);
        return endpoints;
    }
}
