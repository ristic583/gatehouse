using Gatehouse.Application.Tenancy.Onboarding.CompleteOnboarding;
using Gatehouse.Application.Tenancy.Onboarding.GetOnboardingStatus;
using Gatehouse.Application.Tenancy.Onboarding.SkipOnboardingStep;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Gatehouse.Application.Tenancy.Onboarding;

public static class OnboardingModule
{
    /// <summary>Needs the identity-provider store and an <c>IConnectorRegistrar</c>.</summary>
    public static IServiceCollection AddOnboardingModule(this IServiceCollection services)
    {
        services.TryAddScoped<OnboardingStatusReadModel>();
        services.TryAddScoped<OnboardingAudit>();
        services.TryAddScoped<GetOnboardingStatusHandler>();
        services.TryAddScoped<SkipOnboardingStepHandler>();
        services.TryAddScoped<CompleteOnboardingHandler>();
        return services;
    }
}
