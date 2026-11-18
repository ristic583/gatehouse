using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.People.Lifecycle;

/// <summary>Admin lifecycle routes (<c>TenantAdmin</c> policy): run now, read and change the six settings.</summary>
public sealed class LifecycleAdminHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    ITenantContext tenant,
    LifecycleRunner runner,
    LifecycleSettingsStore store,
    IAuditLog audit,
    TimeProvider time)
{
    public Task<LifecycleRunResult> RunAsync(CancellationToken ct) => runner.RunTenantAsync(time.GetUtcNow(), ct);

    public async Task<LifecycleSettingsDto> GetSettingsAsync(CancellationToken ct) => ToDto(await store.GetAsync(ct));

    public async Task<Result<LifecycleSettingsDto>> SetAsync(SetLifecycleSettingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var set = await store.SetAsync(request.Key ?? string.Empty, request.Days, time.GetUtcNow(), ct);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        audit.Record(
            AuditActor.ForPerson(me), "lifecycle.settings_changed", "Tenant", tenant.TenantId.Value,
            new { key = request.Key, value = request.Days });
        await db.SaveChangesAsync(ct);
        return ToDto(await store.GetAsync(ct));
    }

    private static LifecycleSettingsDto ToDto(LifecycleSettings s) =>
        new(s.MaxSponsorshipDays, s.MaxVisitorDays, s.RenewalEmployeeDays, s.RenewalExternalDays, s.RenewalGuestDays, s.RenewalLeadDays);
}
