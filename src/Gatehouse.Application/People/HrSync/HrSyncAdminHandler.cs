using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.People.HrSync;

/// <summary><c>POST /api/v1/admin/hr-sync</c>: runs <see cref="HrSyncRunner"/> now as the calling admin.</summary>
public sealed class HrSyncAdminHandler(HrSyncRunner sync, ICurrentUser currentUser, TimeProvider time)
{
    public async Task<Result<HrSyncResult>> RunAsync(CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        return await sync.RunAsync(AuditActor.ForPerson(me), time.GetUtcNow(), ct);
    }
}
