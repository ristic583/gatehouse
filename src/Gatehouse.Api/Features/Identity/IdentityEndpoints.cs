using System.Security.Claims;
using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Common;

namespace Gatehouse.Api.Features.Identity;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/v1/me", GetMeAsync)
            .RequireAuthorization(Policies.TenantMember)
            .WithName("GetMe");

        return endpoints;
    }

    private static async Task<IResult> GetMeAsync(
        ClaimsPrincipal user,
        ITenantContext tenantContext,
        ICatalogReader catalog,
        CurrentUserAccessor currentUser,
        RoleResolver roles,
        Gatehouse.Application.Tenancy.Onboarding.OnboardingStatusReadModel onboarding,
        CancellationToken ct)
    {
        var tenant = await catalog.FindAsync(tenantContext.TenantId, ct);
        if (tenant is null)
        {
            return Error.Forbidden("tenant.unknown", "The tenant is not known.").ToProblem();
        }

        var email = user.FindFirst("email")?.Value ?? string.Empty;
        var givenName = user.FindFirst("given_name")?.Value;
        var familyName = user.FindFirst("family_name")?.Value;
        var displayName = string.Join(' ', new[] { givenName, familyName }.Where(n => !string.IsNullOrWhiteSpace(n)));

        MePersonDto? person = null;
        IReadOnlyList<string> roleNames = [];
        if (currentUser.PersonId is { } personId)
        {
            person = new MePersonDto(
                personId.Value, currentUser.PersonDisplayName ?? email, currentUser.PersonType?.ToString() ?? string.Empty);
            roleNames = await roles.GetRolesAsync(personId, ct);
        }

        return Results.Ok(new MeDto(
            user.FindFirst("sub")?.Value ?? string.Empty,
            email,
            string.IsNullOrEmpty(displayName) ? email : displayName,
            tenant.Id.Value,
            tenant.DisplayName,
            person,
            roleNames,
            currentUser.IsTenantAdmin && await onboarding.IsPendingAsync(ct)));
    }
}
