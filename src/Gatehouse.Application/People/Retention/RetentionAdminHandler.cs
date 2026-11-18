using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.People.Retention;

/// <summary>Admin retention routes (<c>TenantAdmin</c>): read/change settings, run now.</summary>
public sealed class RetentionAdminHandler(
    ITenantDb db,
    RetentionSettingsStore store,
    RetentionRunner runner,
    ICurrentUser currentUser,
    ITenantContext tenant,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<RetentionSettingsDto> GetAsync(CancellationToken ct)
    {
        var s = await store.GetAsync(ct);
        return new RetentionSettingsDto(s.PhotoDaysAfterTermination, s.PersonalDataDaysAfterTermination, s.DoorEventDays);
    }

    public async Task<Result<RetentionSettingsDto>> SetAsync(SetRetentionSettingRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        var set = await store.SetAsync(request.Key, request.Days, time.GetUtcNow(), ct);
        if (set.IsFailure)
        {
            return set.Error;
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        audit.Record(
            AuditActor.ForPerson(me), "retention.settings_changed", "Tenant", tenant.TenantId.Value,
            new { key = request.Key, value = request.Days });
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public Task<RetentionRunResult> RunAsync(CancellationToken ct) => runner.RunTenantAsync(time.GetUtcNow(), ct);
}
