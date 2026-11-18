using System.Security.Claims;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.RevealPin;

/// <summary>
/// <c>POST /api/v1/tasks/{taskId}/pin-reveal</c> (<c>SecurityStaff</c> + <see cref="RevealPinRequirement"/>): the claimant of an InProgress
/// <c>PinReveal</c> task, who is not its subject, decrypts the subject's PIN. Audits <c>pin.revealed</c> without the value.
/// </summary>
public sealed class RevealPinHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    IPinVault vault,
    IAuditLog audit,
    TimeProvider time)
{
    public const int MaskAfterSeconds = 30;

    public async Task<Result<RevealedPinDto>> HandleAsync(Guid taskId, CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var id = new StaffTaskId(taskId);
        var task = await db.StaffTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null || task.SubjectId == me.Value)
        {
            return Forbidden();
        }

        var resource = new StaffTaskResource(task.Id, task.Kind, task.SiteId, task.AssignedToId, task.Status);
        if (!(await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new RevealPinRequirement())).Succeeded)
        {
            return Forbidden();
        }

        var subject = new PersonId(task.SubjectId);
        var credential = await db.PinCredentials.SingleOrDefaultAsync(p => p.Id == subject, ct);
        if (credential is null)
        {
            return Error.NotFound("pin.not_set", "No PIN is set.");
        }

        var pin = await vault.DecryptAsync(credential.Ciphertext, credential.KeyId, ct);
        credential.RecordReveal(me, task.Id, time.GetUtcNow());
        audit.Record(AuditActor.ForPerson(me), "pin.revealed", "Person", subject.Value.ToString(), new { taskId = task.Id.Value });
        await db.SaveChangesAsync(ct);
        return new RevealedPinDto(pin.Reveal(), MaskAfterSeconds);
    }

    private static Error Forbidden() => Error.Forbidden("auth.forbidden", "You are not allowed to reveal this PIN.");
}
