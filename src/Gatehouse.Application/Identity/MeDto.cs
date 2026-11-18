namespace Gatehouse.Application.Identity;

public sealed record MeDto(
    string Subject,
    string Email,
    string DisplayName,
    string TenantId,
    string TenantName,
    MePersonDto? Person,
    IReadOnlyList<string> Roles,
    bool OnboardingPending = false);
