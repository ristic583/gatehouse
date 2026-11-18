using System.Text.Json;
using Gatehouse.Application.Common.Diagnostics;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Connectors;

/// <summary>One poll iteration in the tenant scope: picks the next commands of the PACS instance and marks them delivered.</summary>
public sealed class ConnectorCommandDelivery(ITenantDb db, IOutboxWriter outbox)
{
    public async Task<IReadOnlyList<CommandEnvelope>> SelectAndDeliverAsync(
        ConnectorContext connector, int maxBatch, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(connector);
        var pacsId = connector.PacsInstanceId;
        var open = await db.ConnectorCommands
            .Where(c => c.PacsInstanceId == pacsId
                && (c.Status == ConnectorCommandStatus.Queued || c.Status == ConnectorCommandStatus.Delivered))
            .ToListAsync(ct);

        foreach (var expired in open.Where(c => c.Status == ConnectorCommandStatus.Delivered && c.LeaseUntil < now))
        {
            if (expired.ExpireLease(now) == CommandOutcome.Failed)
            {
                outbox.Enqueue(
                    new ConnectorCommandAcked(
                        connector.TenantId.Value, pacsId, connector.ConnectorId, now, expired.Id, expired.FulfillmentId,
                        AckStatus.Failed, null, new AckError("connector.lease_expired", "Command lease expired.", false), true),
                    expired.Priority);
            }
        }

        // Grants were held while the PACS capabilities were unknown; one that enforces windows itself can take them now.
        var capabilities = await db.PacsInstances.AsNoTracking()
            .Where(p => p.Id == pacsId)
            .Select(p => p.CapabilitiesJson)
            .FirstOrDefaultAsync(ct);
        if (PacsCapabilityGaps.From(capabilities) is { } gaps && !gaps.Contains(PacsCapabilityGaps.NativeValidity))
        {
            foreach (var deferred in open.Where(c => c.IsDeferred(now)))
            {
                deferred.ReleaseDeferral(now);
            }
        }

        GatehouseTelemetry.QueueDepth.Record(open.Count, new KeyValuePair<string, object?>("pacs_instance", pacsId.Value.ToString()));
        var busy = open.Where(c => c.Status == ConnectorCommandStatus.Delivered).Select(c => c.PartitionKey).ToHashSet();
        // A deferred grant (window starts later) does not hold back the rest of its partition (GH-CONN-01).
        var minSequence = open.Where(c => c.Status == ConnectorCommandStatus.Queued && !c.IsDeferred(now))
            .GroupBy(c => c.PartitionKey)
            .ToDictionary(g => g.Key, g => g.Min(c => c.Sequence));
        var picked = open
            .Where(c => c.Status == ConnectorCommandStatus.Queued && c.NextAttemptAt <= now)
            .Where(c => c.Priority == CommandPriority.High || (!busy.Contains(c.PartitionKey) && c.Sequence == minSequence[c.PartitionKey]))
            .OrderBy(c => c.Priority == CommandPriority.High ? 0 : 1)
            .ThenBy(c => c.CreatedAt)
            .ThenBy(c => c.Sequence)
            .GroupBy(c => c.PartitionKey)
            .Select(g => g.First())
            .OrderBy(c => c.Priority == CommandPriority.High ? 0 : 1)
            .ThenBy(c => c.CreatedAt)
            .Take(maxBatch)
            .ToList();

        var fulfillmentIds = picked.Where(c => c.FulfillmentId is not null).Select(c => c.FulfillmentId!.Value).ToList();
        var fulfillments = fulfillmentIds.Count == 0
            ? []
            : await db.Fulfillments.Where(f => fulfillmentIds.Contains(f.Id)).ToDictionaryAsync(f => f.Id, ct);

        var envelopes = new List<CommandEnvelope>(picked.Count);
        foreach (var command in picked)
        {
            // A grant/revoke whose fulfillment was superseded (revoked, expired, re-planned) must never reach the PACS, even when it
            // was delivered before and came back through a retry or lease expiry (GH-CONN-02).
            if (command.FulfillmentId is { } superseded && fulfillments.TryGetValue(superseded, out var owner)
                && owner.Status == FulfillmentStatus.Superseded)
            {
                command.Supersede(now);
                continue;
            }

            // An interactive unlock past its execute-by deadline is dropped, not executed late (GH-LIVE-01).
            // A missing or unreadable deadline fails closed: queued time + the default deadline (PT-33).
            if (command.Type == ConnectorCommandTypes.MomentaryUnlock
                && (ExecuteBy(command.PayloadJson) ?? command.CreatedAt + MomentaryUnlock.DefaultDeadline) < now)
            {
                command.Expire(DoorCommandExpiredCode, now);
                continue;
            }

            if (!command.Deliver(now).IsSuccess)
            {
                continue;
            }

            if (command.FulfillmentId is { } fid && fulfillments.TryGetValue(fid, out var fulfillment)
                && fulfillment.Status == FulfillmentStatus.Desired)
            {
                fulfillment.MarkPending(command.Id, now);
            }

            using var doc = JsonDocument.Parse(command.PayloadJson);
            envelopes.Add(new CommandEnvelope(
                command.Id.Value,
                command.IdempotencyKey,
                command.Type,
                1,
                connector.TenantId.Value,
                pacsId.Value,
                command.PartitionKey,
                command.Sequence,
                command.Priority.ToString(),
                command.CreatedAt,
                command.LeaseUntil!.Value,
                doc.RootElement.Clone(),
                command.TraceParent));
        }

        await db.SaveChangesAsync(ct);
        return envelopes;
    }

    /// <summary>Error code of a door command that was not delivered before its execute-by deadline.</summary>
    public const string DoorCommandExpiredCode = "door_command.expired";

    private static DateTimeOffset? ExecuteBy(string payloadJson)
    {
        using var doc = JsonDocument.Parse(payloadJson);
        return doc.RootElement.TryGetProperty("executeBy", out var value) && value.ValueKind == JsonValueKind.String
            && value.TryGetDateTimeOffset(out var at)
            ? at
            : null;
    }
}
