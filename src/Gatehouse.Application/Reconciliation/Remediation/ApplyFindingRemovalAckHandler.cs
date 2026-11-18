using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Reconciliation.Remediation;

/// <summary>
/// A final ack of a <c>RemovedFromPacs</c> revoke (GH-LIVE-02): <c>Applied</c> resolves the finding's task (action <c>RemovedFromPacs</c>,
/// audit <c>reconciliation.removal_confirmed</c>); a failure leaves it open (audit <c>reconciliation.removal_failed</c>) so staff retry
/// or dismiss it. Other acks are ignored; the consumer saves.
/// </summary>
public sealed class ApplyFindingRemovalAckHandler(ITenantDb db, IAuditLog audit)
{
    public async Task<Result> HandleAsync(ConnectorCommandAcked ack, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (!ack.IsFinal || ack.FulfillmentId is not null)
        {
            return Result.Success();
        }

        var command = await db.ConnectorCommands.AsNoTracking()
            .Where(c => c.Id == ack.CommandId && c.PacsInstanceId == ack.PacsInstanceId && c.Type == ConnectorCommandTypes.RevokeAccess)
            .Select(c => new { c.IdempotencyKey })
            .FirstOrDefaultAsync(ct);
        if (command is null || ConnectorCommandFactory.FindingOfRevokeKey(command.IdempotencyKey) is not { } findingId)
        {
            return Result.Success();
        }

        var actor = AuditActor.ForAgent(ack.ConnectorId);
        if (ack.Status != AckStatus.Applied)
        {
            audit.Record(actor, "reconciliation.removal_failed", ReconciliationRemediation.AuditSubject, findingId.Value.ToString(),
                new { commandId = ack.CommandId.Value, code = ack.Error?.Code });
            return Result.Success();
        }

        var tasks = await db.StaffTasks
            .Where(t => t.Kind == StaffTaskKind.ReconciliationFinding && t.SubjectId == findingId.Value
                && t.Status != StaffTaskStatus.Resolved)
            .ToListAsync(ct);
        foreach (var task in tasks)
        {
            const string Note = "Removal confirmed by the connector.";
            if (task.Resolve(task.AssignedToId, StaffTaskResolutionAction.RemovedFromPacs, Note, ack.ReceivedAt).IsSuccess)
            {
                audit.Record(actor, "task.resolved", AuditSubjects.StaffTask, task.Id.Value.ToString(),
                    new { action = nameof(StaffTaskResolutionAction.RemovedFromPacs), note = "Removal confirmed by the connector." });
            }
        }

        audit.Record(actor, "reconciliation.removal_confirmed", ReconciliationRemediation.AuditSubject, findingId.Value.ToString(),
            new { commandId = ack.CommandId.Value });
        return Result.Success();
    }
}
