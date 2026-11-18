using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;

namespace Gatehouse.Application.Tenancy.Onboarding.CompleteOnboarding;

/// <summary>
/// <c>POST /api/v1/admin/onboarding/complete</c>: idempotent once completed; every required step must be complete or skipped;
/// audit <c>onboarding.completed</c>.
/// </summary>
public sealed class CompleteOnboardingHandler(ITenantDb db, OnboardingStatusReadModel status, OnboardingAudit audit, TimeProvider time)
{
    public async Task<Result<OnboardingStatusDto>> HandleAsync(CompleteOnboardingCommand command, CancellationToken ct)
    {
        var current = await status.GetAsync(ct);
        if (current.CompletedAt is not null)
        {
            return current;
        }

        if (!current.CanComplete)
        {
            return Error.Conflict("onboarding.incomplete", "Complete every required onboarding step first.");
        }

        var now = time.GetUtcNow();
        var value = now.ToString("O", CultureInfo.InvariantCulture);
        db.TenantSettings.Add(new TenantSetting(OnboardingStatusReadModel.CompletedKey, value, now));
        await audit.RecordAsync("onboarding.completed", null, ct);
        await db.SaveChangesAsync(ct);
        return await status.GetAsync(ct);
    }
}
