namespace Gatehouse.Application.Tenancy.Onboarding;

/// <summary><c>domain-model.md</c> §2.10, in this order.</summary>
public enum OnboardingStep
{
    IdentityProvider,
    Sites,
    People,
    Pacs,
    Connector,
    Staff,
}
