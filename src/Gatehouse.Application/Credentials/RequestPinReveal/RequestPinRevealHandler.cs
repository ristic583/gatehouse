using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tasks;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.RequestPinReveal;

/// <summary><c>POST /api/v1/me/pin/reveal-requests</c>: opens (or reuses) one tenant-wide <c>PinReveal</c> task for me.</summary>
public sealed class RequestPinRevealHandler(
    ITenantDb db, ICurrentUser currentUser, StaffTaskService tasks, IAuditLog audit, TimeProvider time)
{
    public async Task<Result<PinRevealRequestedDto>> HandleAsync(CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        if (!await db.PinCredentials.AnyAsync(p => p.Id == me, ct))
        {
            return Error.NotFound("pin.not_set", "No PIN is set.");
        }

        var actor = AuditActor.ForPerson(me);
        await tasks.OpenAsync(StaffTaskKind.PinReveal, null, "Person", me.Value, actor, time.GetUtcNow(), ct);
        var task = db.StaffTasks.Local.FirstOrDefault(t => t.Kind == StaffTaskKind.PinReveal && t.SubjectId == me.Value
            && t.Status != StaffTaskStatus.Resolved)
            ?? await db.StaffTasks.FirstAsync(t => t.Kind == StaffTaskKind.PinReveal && t.SubjectId == me.Value
            && t.Status != StaffTaskStatus.Resolved, ct);
        audit.Record(actor, "pin.reveal_requested", "Person", me.Value.ToString(), new { taskId = task.Id.Value });
        await db.SaveChangesAsync(ct);
        return new PinRevealRequestedDto(task.Id.Value);
    }
}
