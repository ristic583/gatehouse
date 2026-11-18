using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.OnboardPerson;

/// <summary>
/// <c>POST /api/v1/people</c>: employees (and tenant admins) onboard local externals, visitors and guests within the tenant caps. The
/// sponsor defaults to the caller; only tenant admins name another sponsor. Audits <c>person.onboarded</c> (+ <c>card.assigned</c>) in one
/// save.
/// </summary>
public sealed class OnboardPersonHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    LifecycleSettingsStore settings,
    PersonDetails details,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<PersonDetailDto>> HandleAsync(OnboardPersonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var request = command.Request;
        var meId = await currentUser.RequirePersonIdAsync(ct);
        var me = await db.Persons.AsNoTracking().SingleAsync(p => p.Id == meId, ct);
        if (me.Type != PersonType.Employee && !currentUser.IsTenantAdmin)
        {
            return Forbidden();
        }

        var sponsorId = request.SponsorId is { } sid ? new PersonId(sid) : meId;
        if (sponsorId != meId && !currentUser.IsTenantAdmin)
        {
            return Forbidden();
        }

        var sponsor = sponsorId == meId ? me : await db.Persons.AsNoTracking().FirstOrDefaultAsync(p => p.Id == sponsorId, ct);
        if (sponsor is null)
        {
            return Error.NotFound("person.not_found", "Sponsor not found.");
        }

        if (sponsor.Status == PersonStatus.Terminated)
        {
            return Error.Conflict("person.terminated", "The sponsor is terminated.");
        }

        var type = Enum.Parse<PersonType>(request.Type);
        var eligibility = Eligibility.Create(request.Start, request.End, request.TimeZoneId);
        if (eligibility.IsFailure)
        {
            return eligibility.Error;
        }

        var caps = Person.EnsureWithinCaps(type, eligibility.Value, await settings.GetAsync(ct));
        if (caps.IsFailure)
        {
            return caps.Error;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await db.Persons.AnyAsync(p => p.Email == email, ct))
        {
            return Error.Conflict("person.email_taken", "A person with this email already exists.");
        }

        var now = time.GetUtcNow();
        var created = Person.Create(
            PersonSource.Local, type, request.GivenName, request.FamilyName, email, eligibility.Value, null, sponsor, now);
        if (created.IsFailure)
        {
            return created.Error;
        }

        var person = created.Value;
        db.Persons.Add(person);
        audit.Record(AuditActor.ForPerson(meId), "person.onboarded", "Person", person.Id.Value.ToString(),
            new
            {
                type = type.ToString(),
                sponsorId = sponsor.Id.Value.ToString(),
                start = eligibility.Value.Start,
                end = eligibility.Value.End,
            });

        if (!string.IsNullOrEmpty(request.CardNumber))
        {
            var number = request.CardNumber.Trim();
            if (await db.Cards.AnyAsync(c => c.CardNumber == number && c.State != CardState.Revoked, ct))
            {
                return Error.Conflict("card.number_taken", "Another card uses this number.");
            }

            var card = Card.Create(person.Id, number, now);
            if (card.IsFailure)
            {
                return card.Error;
            }

            db.Cards.Add(card.Value);
            audit.Record(AuditActor.ForPerson(meId), "card.assigned", "Card", card.Value.Id.Value.ToString(),
                new { personId = person.Id.Value.ToString(), cardNumberMasked = CardNumberMask.Mask(number) });
        }

        await db.SaveChangesAsync(ct);
        return await details.BuildAsync(person, ct);
    }

    private static Error Forbidden() => Error.Forbidden("auth.forbidden", "You are not allowed to onboard this person.");
}
