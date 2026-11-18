using Gatehouse.Api.Http;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Tenancy.Onboarding.CompleteOnboarding;
using Gatehouse.Application.Tenancy.Onboarding.GetOnboardingStatus;
using Gatehouse.Application.Tenancy.Onboarding.SkipOnboardingStep;

namespace Gatehouse.Api.Features.Identity;

/// <summary>Tenant admin onboarding status (authorization.md §11).</summary>
public static class OnboardingEndpoints
{
    public static IEndpointRouteBuilder MapOnboardingEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var admin = endpoints.MapGroup("/api/v1/admin/onboarding").RequireAuthorization(Policies.TenantAdmin);

        admin.MapGet(string.Empty, async (GetOnboardingStatusHandler handler, CancellationToken ct) =>
                (await handler.HandleAsync(new GetOnboardingStatusQuery(), ct)).ToHttpResult(Results.Ok))
            .WithName("GetOnboardingStatus");
        admin.MapPost("/steps/{step}/skip", async (string step, SkipOnboardingStepHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new SkipOnboardingStepCommand(step), ct)).ToHttpResult(Results.Ok));
        admin.MapPost("/complete", async (CompleteOnboardingHandler handler, CancellationToken ct) =>
            (await handler.HandleAsync(new CompleteOnboardingCommand(), ct)).ToHttpResult(Results.Ok));

        return endpoints;
    }
}
