using Gatehouse.Domain.Common;

namespace Gatehouse.Application.Tenancy.Onboarding.GetOnboardingStatus;

/// <summary><c>GET /api/v1/admin/onboarding</c>.</summary>
public sealed class GetOnboardingStatusHandler(OnboardingStatusReadModel status)
{
    public async Task<Result<OnboardingStatusDto>> HandleAsync(GetOnboardingStatusQuery query, CancellationToken ct) =>
        await status.GetAsync(ct);
}
