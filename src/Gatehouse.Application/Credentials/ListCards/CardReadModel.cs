using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Connector.Contracts;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Credentials;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ListCards;

/// <summary>Builds <see cref="CardDto"/>s; the PACS status comes from the newest card command batch of each card.</summary>
public sealed class CardReadModel(ITenantDb db)
{
    public async Task<IReadOnlyList<CardDto>> ToDtosAsync(IReadOnlyList<Card> cards, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (cards.Count == 0)
        {
            return [];
        }

        var partitions = cards.Select(c => ConnectorCommandFactory.PartitionKey(c.PersonId)).Distinct().ToList();
        var commands = await db.ConnectorCommands.AsNoTracking()
            .Where(c => partitions.Contains(c.PartitionKey)
            && (c.Type == ConnectorCommandTypes.ActivateCard || c.Type == ConnectorCommandTypes.SuspendCard))
            .Select(c => new CommandRow(c.IdempotencyKey, c.Status, c.CreatedAt))
            .ToListAsync(ct);

        return cards.Select(c => new CardDto(
            c.Id.Value, c.PersonId.Value, CardNumberMask.Mask(c.CardNumber), c.State.ToString(), c.StateChangedAt, PacsStatus(c, commands)))
            .ToList();
    }

    public async Task<CardDto> ToDtoAsync(Card card, CancellationToken ct) => (await ToDtosAsync([card], ct))[0];

    private static string? PacsStatus(Card card, List<CommandRow> commands)
    {
        var activate = CardCommandService.KeyPrefix(ConnectorCommandTypes.ActivateCard, card.Id.Value);
        var suspend = CardCommandService.KeyPrefix(ConnectorCommandTypes.SuspendCard, card.Id.Value);
        var mine = commands.Where(c => c.Key.StartsWith(activate, StringComparison.Ordinal)
            || c.Key.StartsWith(suspend, StringComparison.Ordinal))
            .ToList();
        if (mine.Count == 0)
        {
            return null;
        }

        // Keys are {Type}:{cardId}:{batchId}:{instanceId}; one batch per request.
        var newest = mine.MaxBy(c => c.CreatedAt)!;
        var batch = newest.Key[..(newest.Key.LastIndexOf(':') + 1)];
        var statuses = mine.Where(c => c.Key.StartsWith(batch, StringComparison.Ordinal)).Select(c => c.Status).ToList();
        if (statuses.Contains(ConnectorCommandStatus.Failed))
        {
            return "Failed";
        }

        if (statuses.Any(s => s is ConnectorCommandStatus.Queued or ConnectorCommandStatus.Delivered))
        {
            return "Pending";
        }

        return statuses.Contains(ConnectorCommandStatus.Acked) ? "Confirmed" : null;
    }

    private sealed record CommandRow(string Key, ConnectorCommandStatus Status, DateTimeOffset CreatedAt);
}
