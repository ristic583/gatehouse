using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.CardCommands;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ActivateCard;

/// <summary><c>POST /api/v1/cards/{cardId}/activate</c> (<c>ActivateCardRequirement</c>). Queues <c>ActivateCard</c>; never touches
/// entitlements.</summary>
public sealed class ActivateCardHandler(
    ITenantDb db, ICurrentUser currentUser, CardAccess access, CardCommandService cardCommands, CardReadModel readModel, TimeProvider time)
{
    public async Task<Result<CardDto>> HandleAsync(ActivateCardCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var card = await db.Cards.FirstOrDefaultAsync(c => c.Id == command.CardId, ct);
        var person = card is null ? null : await access.FindPersonAsync(card.PersonId, ct);
        var resource = person is null ? null : CardAccess.ResourceFor(person);
        if (card is null || person is null || !await access.CanSeeAsync(resource!))
        {
            return CardErrors.NotFound();
        }

        if (!await access.AllowedAsync(resource!, new ActivateCardRequirement()))
        {
            return CardAccess.Forbidden();
        }

        if (person.Status == PersonStatus.Terminated)
        {
            return CardAccess.Terminated();
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        var queued = await cardCommands.ActivateAsync(card, me, time.GetUtcNow(), ct);
        if (queued.IsFailure)
        {
            return queued.Error;
        }

        await db.SaveChangesAsync(ct);
        await cardCommands.NotifyAsync(await CardErrors.InstancesOfAsync(db, queued.Value, ct), ct);
        return await readModel.ToDtoAsync(card, ct);
    }
}
