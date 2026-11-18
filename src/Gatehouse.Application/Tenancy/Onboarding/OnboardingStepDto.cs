namespace Gatehouse.Application.Tenancy.Onboarding;

public sealed record OnboardingStepDto(OnboardingStep Step, OnboardingStepState State, bool Skippable, string Detail, string Href);
