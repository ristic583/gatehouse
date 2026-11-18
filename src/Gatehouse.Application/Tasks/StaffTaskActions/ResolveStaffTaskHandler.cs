using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.Remediation;
using Gatehouse.Application.Tasks.Dtos;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Reconciliation;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Tasks.StaffTaskActions;

/// <summary>
/// Resolves a task. M3 kinds allow <c>Retried</c>, <c>ConfirmedManually</c>, <c>VerifiedInPacs</c>, <c>Dismissed</c>; reconciliation
/// finding and drift alert tasks are remediated first (domain-model.md §2.7) before <c>Resolve</c>. M8 <c>PinReveal</c> tasks allow
/// <c>ConfirmedManually</c> and <c>Dismissed</c> (with a note).
/// </summary>
public sealed class ResolveStaffTaskHandler(
    StaffTaskActionService service,
    ITenantDb db,
    ConnectorCommandFactory factory,
    IAuditLog audit)
{
    private static readonly StaffTaskResolutionAction[] Allowed =
    [
        StaffTaskResolutionAction.Retried,
        StaffTaskResolutionAction.ConfirmedManually,
        StaffTaskResolutionAction.VerifiedInPacs,
        StaffTaskResolutionAction.Dismissed,
        StaffTaskResolutionAction.Remediated,
        StaffTaskResolutionAction.RemovedFromPacs,
    ];

    private static readonly StaffTaskResolutionAction[] M7Only =
    [
        StaffTaskResolutionAction.Remediated,
        StaffTaskResolutionAction.RemovedFromPacs,
    ];

    private static readonly StaffTaskResolutionAction[] PinRevealActions =
    [
        StaffTaskResolutionAction.ConfirmedManually,
        StaffTaskResolutionAction.Dismissed,
    ];

    public Task<Result<StaffTaskDto>> HandleAsync(Guid taskId, ResolveStaffTaskDto? body, CancellationToken ct)
    {
        if (body is null
            || !Enum.TryParse<StaffTaskResolutionAction>(body.Action, true, out var action)
            || !Allowed.Contains(action)
            || body.Note is { Length: > 500 })
        {
            return Task.FromResult<Result<StaffTaskDto>>(Required());
        }

        var note = body.Note?.Trim();
        return service.RunAsync(
            taskId,
            async (t, me, now, c) =>
            {
                if (ReconciliationRemediation.Handles(t.Kind))
                {
                    var applied = await RemediateAsync(t, action, note, me, now, c);
                    if (applied.IsFailure)
                    {
                        return applied;
                    }

                    if (action == StaffTaskResolutionAction.RemovedFromPacs)
                    {
                        // Resolved only once the connector confirms the removal (GH-LIVE-02); until then the task stays with this
                        // staff member.
                        return t.Status == StaffTaskStatus.Open ? t.Claim(me, now) : Result.Success();
                    }
                }
                else if (t.Kind == StaffTaskKind.PinReveal)
                {
                    if (!PinRevealActions.Contains(action)
                        || (action == StaffTaskResolutionAction.Dismissed && string.IsNullOrEmpty(note)))
                    {
                        return Required();
                    }
                }
                else if (M7Only.Contains(action))
                {
                    return Required();
                }

                return t.Resolve(me, action, note, now);
            },
            action == StaffTaskResolutionAction.RemovedFromPacs ? "task.removal_requested" : "task.resolved",
            _ => new { action = action.ToString(), note },
            ct);
    }

    /// <summary>
    /// Resolution action of a finding or drift alert task (domain-model.md §2.7 "Remediation"); the caller saves once.
    /// <c>RemovedFromPacs</c> only queues the removal: the task stays open until the connector confirms it
    /// (<see cref="ApplyFindingRemovalAckHandler"/>, GH-LIVE-02).
    /// </summary>
    private async Task<Result> RemediateAsync(
        StaffTask task,
        StaffTaskResolutionAction action,
        string? note,
        PersonId staff,
        DateTimeOffset now,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(task);
        var hasNote = !string.IsNullOrWhiteSpace(note);
        if (task.Kind == StaffTaskKind.DriftAlert)
        {
            return action == StaffTaskResolutionAction.VerifiedInPacs || (action == StaffTaskResolutionAction.Dismissed && hasNote)
                ? Result.Success()
                : RemediationRequired();
        }

        if (task.Kind != StaffTaskKind.ReconciliationFinding)
        {
            return Result.Success();
        }

        var findingId = new ReconciliationFindingId(task.SubjectId);
        var finding = await db.ReconciliationFindings.AsNoTracking().FirstOrDefaultAsync(f => f.Id == findingId, ct);
        if (finding is null)
        {
            return RemediationRequired();
        }

        var grantKind = finding.Kind is FindingKind.MissingInPacs or FindingKind.Mismatch;
        Guid? commandId = null;
        switch (action)
        {
            case StaffTaskResolutionAction.Dismissed when hasNote:
                break;
            case StaffTaskResolutionAction.Remediated or StaffTaskResolutionAction.VerifiedInPacs when grantKind:
                var fulfillment = finding.FulfillmentId is { } fid
                    ? await db.Fulfillments.FirstOrDefaultAsync(f => f.Id == fid, ct)
                    : null;
                if (fulfillment is null)
                {
                    return RemediationRequired();
                }

                var applied = action == StaffTaskResolutionAction.Remediated
                    ? fulfillment.Remediate(now)
                    : fulfillment.ResolveAsInSync(staff, now);
                if (applied.IsFailure)
                {
                    return applied;
                }

                break;
            case StaffTaskResolutionAction.RemovedFromPacs when !grantKind:
                var instanceId = await db.ReconciliationReports.AsNoTracking()
                    .Where(r => r.Id == finding.ReportId).Select(r => r.PacsInstanceId).FirstAsync(ct);
                // A retry after a failed removal needs a new idempotency key: the agent remembers the failed attempt.
                var prefix = ConnectorCommandFactory.FindingRevokeKey(finding.Id, 0)[..^1];
                var attempts = await db.ConnectorCommands.CountAsync(c => c.IdempotencyKey.StartsWith(prefix), ct);
                var command = await factory.CreateForFindingRevokeAsync(instanceId, finding, ct, attempts + 1);
                db.ConnectorCommands.Add(command);
                commandId = command.Id.Value;
                break;
            default:
                return RemediationRequired();
        }

        var subject = finding.Id.Value.ToString();
        audit.Record(AuditActor.ForPerson(staff), "reconciliation.remediated", ReconciliationRemediation.AuditSubject, subject, new
        {
            taskId = task.Id.Value,
            kind = finding.Kind.ToString(),
            action = action.ToString(),
            fulfillmentId = finding.FulfillmentId?.Value,
            commandId,
        });
        return Result.Success();
    }

    private static Error Required() =>
        Error.Validation(
            "task.resolution_required",
            "A valid resolution action for this task and a note of at most 500 characters are required.");

    private static Error RemediationRequired() =>
        Error.Validation("task.resolution_required", "This resolution action is not allowed for this task, or a note is required.");
}
