using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Application.Tasks;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Gatehouse.Domain.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Connectors;

public sealed class RecordHeartbeatHandler(
    ITenantDb db,
    ConnectorContext connector,
    IOutboxWriter outbox,
    StaffTaskService tasks,
    IAuditLog audit,
    TimeProvider time)
{
    private const int PollWaitSecondsHint = 25;

    public async Task<Result<HeartbeatResponse>> HandleAsync(RecordHeartbeatCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var now = time.GetUtcNow();
        var heartbeat = command.Heartbeat;
        var pacs = await db.PacsInstances.SingleOrDefaultAsync(p => p.Id == connector.PacsInstanceId, ct);
        if (pacs is null)
        {
            return Error.NotFound("connector.scope_mismatch", "PACS instance not found.");
        }

        var json = JsonSerializer.Serialize(heartbeat.Capabilities, ConnectorJson.Options);

        // jsonb normalises text, so compare semantically and hand the stored form back when nothing changed.
        if (pacs.CapabilitiesJson is { } stored && JsonNode.DeepEquals(JsonNode.Parse(stored), JsonNode.Parse(json)))
        {
            json = stored;
        }

        if (pacs.RecordHeartbeat(now, heartbeat.AgentVersion, json))
        {
            outbox.Enqueue(
                new ConnectorCapabilitiesReported(connector.TenantId.Value, pacs.Id, connector.ConnectorId, now, heartbeat.Capabilities),
                CommandPriority.Normal);
        }

        outbox.Enqueue(
            new ConnectorHeartbeatReceived(
                connector.TenantId.Value, pacs.Id, connector.ConnectorId, now, heartbeat.AgentVersion, heartbeat.QueueDepth,
                heartbeat.PacsReachable),
            CommandPriority.Normal);
        if (heartbeat.RefusedRemovals is { } refused)
        {
            await SyncRefusedRemovalTasksAsync(refused, pacs.SiteIds.Select(s => (SiteId?)s).FirstOrDefault(), now, ct);
        }

        await db.SaveChangesAsync(ct);
        return new HeartbeatResponse(now, PollWaitSecondsHint, null);
    }

    /// <summary>Subject type of <c>ScheduledRemovalRefused</c> tasks: one namespace per connector, so its open tasks can be
    /// listed.</summary>
    public static string RefusedRemovalSubjectType(ConnectorId connectorId) => $"ScheduledRemoval:{connectorId.Value:N}";

    /// <summary>Stable subject id per (connector, revocationRef), so the unique open-task index makes reporting idempotent.</summary>
    public static Guid RefusedRemovalSubjectId(ConnectorId connectorId, string revocationRef)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{connectorId.Value:N}|{revocationRef}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>GH-CONN-03: one open task per refused local removal; resolved once the agent stops reporting it (removal
    /// applied).</summary>
    private async Task SyncRefusedRemovalTasksAsync(
        IReadOnlyList<RefusedRemoval> refused, SiteId? siteId, DateTimeOffset now, CancellationToken ct)
    {
        var subjectType = RefusedRemovalSubjectType(connector.ConnectorId);
        var actor = AuditActor.ForAgent(connector.ConnectorId);
        var open = await db.StaffTasks
            .Where(t => t.Kind == StaffTaskKind.ScheduledRemovalRefused && t.SubjectType == subjectType
            && t.Status != StaffTaskStatus.Resolved)
            .Select(t => t.SubjectId)
            .ToListAsync(ct);
        var reported = new HashSet<Guid>();
        foreach (var removal in refused.Where(r => !string.IsNullOrWhiteSpace(r.RevocationRef))
            .DistinctBy(r => r.RevocationRef, StringComparer.Ordinal))
        {
            var subjectId = RefusedRemovalSubjectId(connector.ConnectorId, removal.RevocationRef);
            reported.Add(subjectId);
            if (open.Contains(subjectId))
            {
                continue;
            }

            await tasks.OpenAsync(StaffTaskKind.ScheduledRemovalRefused, siteId, subjectType, subjectId, actor, now, ct);
            if (db.StaffTasks.Local.FirstOrDefault(t => t.SubjectId == subjectId
                && t.Kind == StaffTaskKind.ScheduledRemovalRefused) is { } task)
            {
                audit.Record(actor, "task.removal_refused_reported", AuditSubjects.StaffTask, task.Id.Value.ToString(), new
                {
                    revocationRef = removal.RevocationRef,
                    code = removal.Code,
                    firstRefusedAt = removal.FirstRefusedAt.ToString("O", CultureInfo.InvariantCulture),
                    attempts = removal.Attempts,
                });
            }
        }

        foreach (var gone in open.Where(id => !reported.Contains(id)))
        {
            await tasks.ResolveForSubjectAsync(subjectType, gone, null, actor, now, ct);
        }
    }
}
