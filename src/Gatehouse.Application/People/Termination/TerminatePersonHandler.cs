using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Termination;

/// <summary>
/// <c>POST /api/v1/admin/people/{id}/terminate</c> (TenantAdmin): terminates an employee now without the HR feed (lab finding #9),
/// through <see cref="TerminationService"/> (entitlements revoked, cards revoked in every PACS). Externals end via their sponsorship.
/// </summary>
public sealed class TerminatePersonHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    TerminationService termination,
    PersonDetails details,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<PersonDetailDto>> HandleAsync(TerminatePersonCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var person = await db.Persons.SingleOrDefaultAsync(p => p.Id == command.PersonId, ct);
        if (person is null)
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        if (person.Type != PersonType.Employee)
        {
            return Error.Conflict("person.not_employee", "Only employees are terminated here; end an external's sponsorship instead.");
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        if (me == person.Id)
        {
            return Error.Forbidden("person.self_termination_forbidden", "You cannot terminate yourself.");
        }

        var actor = AuditActor.ForPerson(me);
        var now = time.GetUtcNow();
        var outcome = await termination.TerminateAsync(person, actor, now, ct);
        if (outcome.IsFailure)
        {
            return outcome.Error;
        }

        audit.Record(actor, "person.terminated_by_admin", "Person", person.Id.Value.ToString(), new { by = me.Value.ToString() });
        await db.SaveChangesAsync(ct);
        await termination.NotifyAsync(outcome.Value, ct);
        return await details.BuildAsync(person, ct);
    }
}
