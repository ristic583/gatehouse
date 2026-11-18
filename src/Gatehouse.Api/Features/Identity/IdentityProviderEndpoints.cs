using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Identity.IdentityProviders;
using Gatehouse.Domain.Common;

namespace Gatehouse.Api.Features.Identity;

/// <summary>Tenant admin: the organization's single upstream IdP (authorization.md §11, ADR-009 §4).</summary>
public static class IdentityProviderEndpoints
{
    public static IEndpointRouteBuilder MapIdentityProviderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/v1/admin/identity-provider").RequireAuthorization(Policies.TenantAdmin);

        group.MapGet(string.Empty, async (GetIdentityProviderHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);
            return result.IsFailure ? Problem(result.Error) : result.Value is null ? Results.NoContent() : Results.Ok(result.Value);
        }).WithName("GetIdentityProvider");

        group.MapPut(string.Empty, async (SaveIdentityProviderRequest request, SaveIdentityProviderHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.IsFailure ? Problem(result.Error) : Results.Ok(result.Value);
        }).WithName("SaveIdentityProvider");

        group.MapPost("/test", async (SaveIdentityProviderRequest request, TestIdentityProviderHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(request, ct);
            return result.IsFailure ? Problem(result.Error) : Results.Ok(result.Value);
        }).WithName("TestIdentityProvider");

        group.MapDelete(string.Empty, async (RemoveIdentityProviderHandler handler, CancellationToken ct) =>
        {
            var result = await handler.HandleAsync(ct);
            return result.IsFailure ? Problem(result.Error) : Results.NoContent();
        }).WithName("RemoveIdentityProvider");

        return endpoints;
    }

    /// <summary>Keycloak refusing or unreachable is a bad gateway (contract: <c>502 idp.provisioning_failed</c>).</summary>
    private static IResult Problem(Error error) => error.Code == "idp.provisioning_failed"
        ? TypedResults.Problem(
            statusCode: StatusCodes.Status502BadGateway,
            title: error.Message,
            extensions: new Dictionary<string, object?> { ["code"] = error.Code })
        : error.ToProblem();
}
