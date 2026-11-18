using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors.Messages;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Events;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.CardCommands;

/// <summary>
/// Final acks of card commands (M8). <c>ActivateCard</c>: when every command of the batch is <c>Acked</c> the card becomes
/// <c>Active</c> (audit <c>card.activated</c>); a failed/unsupported one returns it to <c>Assigned</c> (<c>card.activation_failed</c>).
/// <c>SuspendCard</c> and <c>SetPin</c> are tracked by the command row only. The consumer transaction saves.
/// </summary>
public sealed class ApplyCardCommandAckHandler(ITenantDb db, IAuditLog audit, TimeProvider time)
{
    public async Task<Result> HandleAsync(ConnectorCommandAcked ack, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ack);
        if (!ack.IsFinal)
        {
            return Result.Success();
        }

        var command = await db.ConnectorCommands.AsNoTracking().FirstOrDefaultAsync(c => c.Id == ack.CommandId, ct);
        if (command is null || command.Type != ConnectorCommandTypes.ActivateCard)
        {
            return Result.Success();
        }

        // Key: ActivateCard:{cardId}:{batch}:{instanceId}
        var parts = command.IdempotencyKey.Split(':');
        if (parts.Length != 4 || !Guid.TryParse(parts[1], out var cardGuid))
        {
            return Result.Success();
        }

        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == new Domain.Credentials.CredentialId(cardGuid), ct);
        if (card is null || card.State != Domain.Credentials.CardState.ActivationPending)
        {
            return Result.Success();
        }

        var now = time.GetUtcNow();
        var actor = AuditActor.ForAgent(ack.ConnectorId);
        if (ack.Status != AckStatus.Applied)
        {
            var failed = card.FailActivation(now);
            if (failed.IsSuccess)
            {
                audit.Record(
                    actor, "card.activation_failed", "Card", card.Id.Value.ToString(),
                    new { personId = card.PersonId.Value.ToString(), errorCode = ack.Error?.Code ?? "connector.failed" });
            }

            return failed;
        }

        var batchPrefix = $"{parts[0]}:{parts[1]}:{parts[2]}:";
        var outstanding = await db.ConnectorCommands.AsNoTracking()
            .AnyAsync(c => c.IdempotencyKey.StartsWith(batchPrefix) && c.Id != command.Id && c.Status != ConnectorCommandStatus.Acked, ct);
        if (outstanding)
        {
            return Result.Success();
        }

        var confirmed = card.ConfirmActivation(now);
        if (confirmed.IsSuccess)
        {
            audit.Record(actor, "card.activated", "Card", card.Id.Value.ToString(), new { personId = card.PersonId.Value.ToString() });
        }

        return confirmed;
    }
}
