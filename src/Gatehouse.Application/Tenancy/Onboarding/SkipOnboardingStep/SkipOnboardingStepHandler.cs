using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tenancy;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tenancy.Onboarding.SkipOnboardingStep;

/// <summary>
/// <c>POST /api/v1/admin/onboarding/steps/{step}/skip</c>: Connector and Staff only; audit <c>onboarding.step_skipped</c>.
/// </summary>
public sealed class SkipOnboardingStepHandler(ITenantDb db, OnboardingStatusReadModel status, OnboardingAudit audit, TimeProvider time)
{
    public async Task<Result<OnboardingStatusDto>> HandleAsync(SkipOnboardingStepCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (!Enum.TryParse<OnboardingStep>(command.Step, ignoreCase: true, out var parsed)
            || !OnboardingStatusReadModel.Skippable.Contains(parsed))
        {
            return Error.Conflict("onboarding.step_not_skippable", "Only the Connector and Staff steps can be skipped.");
        }

        var now = time.GetUtcNow();
        var key = OnboardingStatusReadModel.SkippedPrefix + parsed;
        var row = await db.TenantSettings.SingleOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.TenantSettings.Add(new TenantSetting(key, now.ToString("O", CultureInfo.InvariantCulture), now));
        }

        await audit.RecordAsync("onboarding.step_skipped", new { step = parsed.ToString() }, ct);
        await db.SaveChangesAsync(ct);
        return await status.GetAsync(ct);
    }
}
