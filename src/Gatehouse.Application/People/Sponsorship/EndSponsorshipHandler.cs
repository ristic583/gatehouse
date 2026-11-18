using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Application.People.Termination;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.People.Sponsorship;

/// <summary><c>POST /api/v1/people/{id}/sponsorship/end</c> (<c>ManagePersonRequirement</c>): ends the sponsorship now and terminates the
/// person.</summary>
public sealed class EndSponsorshipHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    ManagedPersonLoader loader,
    TerminationService termination,
    PersonDetails details,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<PersonDetailDto>> HandleAsync(EndSponsorshipCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await loader.LoadAsync(command.PersonId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var person = loaded.Value;
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var sponsorId = person.SponsorId;
        var ended = person.EndSponsorship(me, now);
        if (ended.IsFailure)
        {
            return ended.Error;
        }

        var actor = AuditActor.ForPerson(me);
        audit.Record(actor, "sponsorship.ended", "Person", person.Id.Value.ToString(), new { sponsorId = sponsorId?.Value.ToString() });
        var outcome = await termination.TerminateAsync(person, actor, now, ct);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        await db.SaveChangesAsync(ct);
        await termination.NotifyAsync(outcome.Value, ct);
        return await details.BuildAsync(person, ct);
    }
}
