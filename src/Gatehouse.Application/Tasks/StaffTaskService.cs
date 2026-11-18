using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks;

/// <summary>Opens and auto-resolves staff tasks in-process (ADR-008); callers save once.</summary>
public sealed class StaffTaskService(ITenantDb db, IAuditLog audit)
{
    public async Task OpenAsync(
        StaffTaskKind kind,
        SiteId? siteId,
        string subjectType,
        Guid subjectId,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var pending = db.StaffTasks.Local.Any(t => t.Kind == kind && t.SubjectId == subjectId && t.Status != StaffTaskStatus.Resolved);
        if (pending
            || await db.StaffTasks.AnyAsync(t => t.Kind == kind && t.SubjectId == subjectId && t.Status != StaffTaskStatus.Resolved, ct))
        {
            return;
        }

        var task = StaffTask.Open(kind, siteId, subjectType, subjectId, now);
        db.StaffTasks.Add(task);
        audit.Record(
            actor, "task.created", AuditSubjects.StaffTask, task.Id.Value.ToString(),
            new { kind = kind.ToString(), subjectType, subjectId });
    }

    public async Task ResolveForSubjectAsync(
        string subjectType,
        Guid subjectId,
        PersonId? by,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var tasks = await db.StaffTasks
            .Where(t => t.SubjectType == subjectType && t.SubjectId == subjectId && t.Status != StaffTaskStatus.Resolved)
            .ToListAsync(ct);
        foreach (var task in tasks)
        {
            if (task.Resolve(by, StaffTaskResolutionAction.AutoResolved, null, now).IsFailure)
            {
                continue;
            }

            audit.Record(
                actor, "task.resolved", AuditSubjects.StaffTask, task.Id.Value.ToString(),
                new { action = nameof(StaffTaskResolutionAction.AutoResolved), note = (string?)null });
        }
    }
}
