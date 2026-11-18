namespace Gatehouse.Application.Tenancy.Onboarding;

public sealed record OnboardingStatusDto(IReadOnlyList<OnboardingStepDto> Steps, bool CanComplete, DateTimeOffset? CompletedAt);
