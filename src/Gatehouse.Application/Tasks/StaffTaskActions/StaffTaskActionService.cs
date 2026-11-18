using System.Security.Claims;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks.StaffTaskActions;

public sealed class StaffTaskActionService(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    StaffTaskDtoBuilder builder,
    IAuditLog audit,
    TimeProvider time)
{
    public Task<Result<StaffTaskDto>> RunAsync(
        Guid taskId,
        Func<StaffTask, PersonId, DateTimeOffset, Result> action,
        string auditAction,
        Func<StaffTask, object> payload,
        CancellationToken ct) =>
        RunAsync(taskId, (t, me, now, _) => Task.FromResult(action(t, me, now)), auditAction, payload, ct);

    /// <summary>Same as the synchronous overload; <paramref name="action"/> may load and change other aggregates (one save).</summary>
    public async Task<Result<StaffTaskDto>> RunAsync(
        Guid taskId,
        Func<StaffTask, PersonId, DateTimeOffset, CancellationToken, Task<Result>> action,
        string auditAction,
        Func<StaffTask, object> payload,
        CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var id = new StaffTaskId(taskId);
        var task = await db.StaffTasks.VisibleTo(currentUser).SingleOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
        {
            return Error.NotFound("task.not_found", "Task not found.");
        }

        var resource = new StaffTaskResource(task.Id, task.Kind, task.SiteId, task.AssignedToId, task.Status);
        if (!(await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new HandleStaffTaskRequirement())).Succeeded)
        {
            return Error.Forbidden("auth.forbidden", "You are not allowed to handle this task.");
        }

        var result = await action(task, me, time.GetUtcNow(), ct);
        if (result.IsFailure)
        {
            return result.Error;
        }

        audit.Record(AuditActor.ForPerson(me), auditAction, AuditSubjects.StaffTask, task.Id.Value.ToString(), payload(task));
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("task.invalid_transition", "The task was changed by someone else.");
        }

        return (await builder.BuildAsync([task], ct))[0];
    }
}
