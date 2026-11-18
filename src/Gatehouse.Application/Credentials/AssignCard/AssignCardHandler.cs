using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.AssignCard;

/// <summary><c>POST /api/v1/people/{personId}/cards</c> (<c>ManagePersonRequirement</c> or security staff); the card starts
/// Assigned.</summary>
public sealed class AssignCardHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    CardAccess access,
    CardReadModel readModel,
    FulfillmentPlanner planner,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<CardDto>> HandleAsync(AssignCardCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var person = await access.FindPersonAsync(command.PersonId, ct);
        var resource = person is null ? null : CardAccess.ResourceFor(person);
        if (person is null || !await access.CanSeeAsync(resource!))
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        if (!await access.CanIssueAsync(resource!))
        {
            return CardAccess.Forbidden();
        }

        if (person.Status == PersonStatus.Terminated)
        {
            return CardAccess.Terminated();
        }

        if (await db.Cards.AnyAsync(c => c.PersonId == person.Id && c.State != CardState.Revoked, ct))
        {
            return Error.Conflict("card.already_assigned", "The person already has a card that is not revoked.");
        }

        var number = command.CardNumber?.Trim() ?? string.Empty;
        if (await db.Cards.AnyAsync(c => c.CardNumber == number && c.State != CardState.Revoked, ct))
        {
            return Error.Conflict("card.number_taken", "Another card uses this number.");
        }

        var created = Card.Create(person.Id, number, time.GetUtcNow());
        if (created.IsFailure)
        {
            return created.Error;
        }

        var card = created.Value;
        var me = await currentUser.RequirePersonIdAsync(ct);
        db.Cards.Add(card);
        var actor = AuditActor.ForPerson(me);
        audit.Record(
            actor, "card.assigned", "Card", card.Id.Value.ToString(),
            new { personId = person.Id.Value.ToString(), cardNumberMasked = CardNumberMask.Mask(number) });
        await planner.RetryMissingCardAsync(person.Id, actor, card.StateChangedAt, ct);
        await db.SaveChangesAsync(ct);
        return await readModel.ToDtoAsync(card, ct);
    }
}
