using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.DeactivateCard;

/// <summary><c>POST /api/v1/cards/{cardId}/deactivate</c> (<c>ActivateCardRequirement</c>, <c>ManagePersonRequirement</c> or security
/// staff); queues <c>SuspendCard</c>.</summary>
public sealed class DeactivateCardHandler(
    ITenantDb db, ICurrentUser currentUser, CardAccess access, CardCommandService cardCommands, CardReadModel readModel, IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<CardDto>> HandleAsync(DeactivateCardCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == command.CardId, ct);
        var person = card is null ? null : await access.FindPersonAsync(card.PersonId, ct);
        var resource = person is null ? null : CardAccess.ResourceFor(person);
        if (card is null || person is null || !await access.CanSeeAsync(resource!))
        {
            return CardErrors.NotFound();
        }

        if (!await access.CanDeactivateAsync(resource!))
        {
            return CardAccess.Forbidden();
        }

        if (person.Status == PersonStatus.Terminated)
        {
            return CardAccess.Terminated();
        }

        var reason = command.Reason?.Trim() ?? string.Empty;
        var deactivated = card.Deactivate(reason, time.GetUtcNow());
        if (deactivated.IsFailure)
        {
            return deactivated.Error;
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var commandIds = await cardCommands.QueueSuspensionAsync(card, CardTargetState.Suspended, reason, CommandPriority.Normal, ct);
        audit.Record(AuditActor.ForPerson(me), "card.deactivated", "Card", card.Id.Value.ToString(),
            new { personId = person.Id.Value.ToString(), reason, commandIds = commandIds.Select(c => c.Value.ToString()).ToArray() });
        await db.SaveChangesAsync(ct);
        await cardCommands.NotifyAsync(await CardErrors.InstancesOfAsync(db, commandIds, ct), ct);
        return await readModel.ToDtoAsync(card, ct);
    }
}
