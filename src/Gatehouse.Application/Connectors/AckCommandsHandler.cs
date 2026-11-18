using Gatehouse.Application.Common.Diagnostics;
using Gatehouse.Application.Common.Messaging;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Connector.Contracts.Protocol;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Connectors;

public sealed class AckCommandsHandler(ITenantDb db, ConnectorContext connector, IOutboxWriter outbox, TimeProvider time)
{
    /// <summary>The agent did not run a grant because a High priority revoke of the same access overtook it (GH-CONN-02).</summary>
    public const string GrantFencedCode = "agent.grant_fenced";

    public async Task<Result<AckResponse>> HandleAsync(AckRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Acks is not { Count: >= 1 and <= 100 } acks
            || acks.Any(a => a.Status != AckStatus.Applied && a.Error is null))
        {
            return Error.Validation("connector.validation_failed", "1-100 acks are required; error is required unless Applied.");
        }

        var now = time.GetUtcNow();
        var ids = acks.Select(a => new CommandId(a.CommandId)).Distinct().ToList();
        var commands = await db.ConnectorCommands
            .Where(c => c.PacsInstanceId == connector.PacsInstanceId && ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);

        var results = new List<AckItemResult>(acks.Count);
        foreach (var ack in acks)
        {
            if (!commands.TryGetValue(new CommandId(ack.CommandId), out var command))
            {
                results.Add(new AckItemResult(ack.CommandId, false, "connector.unknown_command"));
                continue;
            }

            if (!string.Equals(command.IdempotencyKey, ack.IdempotencyKey, StringComparison.Ordinal))
            {
                results.Add(new AckItemResult(ack.CommandId, false, "connector.idempotency_key_mismatch"));
                continue;
            }

            var status = ack.Status == AckStatus.Applied
                ? CommandAckStatus.Applied
                : ack.Status == AckStatus.Failed
                ? CommandAckStatus.Failed
                : CommandAckStatus.Unsupported;
            var outcome = command.ApplyAck(status, ack.PacsRef, ack.Error?.Code, ack.Error?.Message, ack.Error?.Retryable ?? false, now);
            switch (outcome)
            {
                case CommandOutcome.Conflict:
                    results.Add(new AckItemResult(ack.CommandId, false, "connector.already_final"));
                    continue;
                case CommandOutcome.Duplicate:
                    results.Add(new AckItemResult(ack.CommandId, true, null));
                    continue;
                case CommandOutcome.Acked:
                    GatehouseTelemetry.ConfirmLatency.Record(
                        Math.Max(0, (now - command.CreatedAt).TotalSeconds),
                        new KeyValuePair<string, object?>("command_type", command.Type));
                    break;
            }

            // A fenced grant was cancelled by the revoke that overtook it: its fulfillment is superseded, never failed (no task).
            var fenced = string.Equals(ack.Error?.Code, GrantFencedCode, StringComparison.Ordinal);
            if (fenced && command.FulfillmentId is { } fulfillmentId
                && await db.Fulfillments.FirstOrDefaultAsync(f => f.Id == fulfillmentId, ct) is { } fulfillment)
            {
                _ = fulfillment.Supersede(now); // already Superseded/Confirmed → nothing to change
            }

            outbox.Enqueue(
                new ConnectorCommandAcked(
                    connector.TenantId.Value, connector.PacsInstanceId, connector.ConnectorId, now, command.Id, command.FulfillmentId,
                    ack.Status, ack.PacsRef, ack.Error, !fenced && (outcome is CommandOutcome.Acked or CommandOutcome.Failed)),
                command.Priority);
            results.Add(new AckItemResult(ack.CommandId, true, null));
        }

        await db.SaveChangesAsync(ct);
        return new AckResponse(results);
    }
}
