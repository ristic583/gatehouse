using System.Text.Json;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;

namespace Gatehouse.Application.Credentials.CardCommands;

/// <summary>
/// Queues card and PIN commands (messages.md §2) for every card command target. Idempotency keys are
/// <c>{Type}:{subjectId}:{batchId}:{instanceId}</c> (the key is globally unique); one batch per request. Callers save, then call
/// <see cref="NotifyAsync"/>.
/// </summary>
public sealed class CardCommandService(
    ITenantDb db,
    CardCommandTargets targets,
    ConnectorCommandFactory factory,
    IConnectorCommandSignal signal,
    ITenantContext tenant,
    IAuditLog audit)
{
    public static string KeyPrefix(string type, Guid subjectId) => $"{type}:{subjectId}:";

    /// <summary>
    /// Requests activation and queues <c>ActivateCard</c> per target (audit <c>card.activation_requested</c>). With no target the card is
    /// activated at once by system <c>card-activation</c> (audit <c>card.activated</c>). Does not save.
    /// </summary>
    public async Task<Result<IReadOnlyList<CommandId>>> ActivateAsync(
        Card card, PersonId requestedBy, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        var requested = card.RequestActivation(requestedBy, now);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        var commandIds = await QueueActivationAsync(card, ct);
        var cardId = card.Id.Value.ToString();
        var personId = card.PersonId.Value.ToString();
        audit.Record(
            AuditActor.ForPerson(requestedBy), "card.activation_requested", "Card", cardId,
            new { personId, commandIds = commandIds.Select(c => c.Value.ToString()).ToArray() });
        if (commandIds.Count == 0)
        {
            card.ConfirmActivation(now);
            audit.Record(AuditActor.ForSystem("card-activation"), "card.activated", "Card", cardId, new { personId });
        }

        return Result.Success<IReadOnlyList<CommandId>>(commandIds);
    }

    public async Task<IReadOnlyList<CommandId>> QueueActivationAsync(Card card, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        var payload = new Connector.Contracts.Commands.ActivateCard(card.PersonId.Value.ToString(), card.CardNumber);
        return await QueueAsync(
            await targets.ForPersonAsync(card.PersonId, ct), card.PersonId, ConnectorCommandTypes.ActivateCard, card.Id.Value, payload,
            CommandPriority.Normal, ct);
    }

    public async Task<IReadOnlyList<CommandId>> QueueSuspensionAsync(
        Card card, CardTargetState target, string reason, CommandPriority priority, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(card);
        var payload = new SuspendCard(card.PersonId.Value.ToString(), card.CardNumber, target, reason);
        return await QueueAsync(
            await targets.ForPersonAsync(card.PersonId, ct), card.PersonId, ConnectorCommandTypes.SuspendCard, card.Id.Value, payload,
            priority, ct);
    }

    /// <summary>Only for targets whose capabilities report <c>PinWrite</c>; <paramref name="vaultCiphertext"/> is re-encrypted by the
    /// gateway.</summary>
    public async Task<IReadOnlyList<CommandId>> QueueSetPinAsync(
        PersonId personId, string cardNumber, string vaultCiphertext, CancellationToken ct)
    {
        var pinTargets = (await targets.ForPersonAsync(personId, ct)).Where(t => SupportsPinWrite(t.CapabilitiesJson)).ToList();
        var payload = new Connector.Contracts.Commands.SetPin(personId.Value.ToString(), cardNumber, vaultCiphertext);
        return await QueueAsync(pinTargets, personId, ConnectorCommandTypes.SetPin, personId.Value, payload, CommandPriority.Normal, ct);
    }

    public async Task NotifyAsync(IEnumerable<PacsInstanceId> instanceIds, CancellationToken ct)
    {
        foreach (var id in instanceIds.Distinct())
        {
            await signal.NotifyAsync(tenant.TenantId, id, ct);
        }
    }

    private static bool SupportsPinWrite(string? capabilitiesJson)
    {
        if (string.IsNullOrEmpty(capabilitiesJson))
        {
            return false;
        }

        try
        {
            return JsonSerializer.Deserialize<PacsCapabilities>(capabilitiesJson, ConnectorJson.Options)?.PinWrite == true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<IReadOnlyList<CommandId>> QueueAsync(
        IReadOnlyList<CardCommandTarget> to,
        PersonId personId,
        string type,
        Guid subjectId,
        object payload,
        CommandPriority priority,
        CancellationToken ct)
    {
        var batch = Guid.CreateVersion7();
        var ids = new List<CommandId>();
        foreach (var target in to)
        {
            var key = $"{KeyPrefix(type, subjectId)}{batch}:{target.PacsInstanceId.Value}";
            var command = await factory.CreateForPersonAsync(target.PacsInstanceId, personId, type, payload, key, priority, ct);
            db.ConnectorCommands.Add(command);
            ids.Add(command.Id);
        }

        return ids;
    }
}
