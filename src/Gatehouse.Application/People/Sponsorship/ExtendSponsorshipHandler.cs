using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Application.People.GetPerson;
using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;

namespace Gatehouse.Application.People.Sponsorship;

/// <summary><c>PUT /api/v1/people/{id}/eligibility</c> (<c>ManagePersonRequirement</c>): changes a local person's end within the
/// caps.</summary>
public sealed class ExtendSponsorshipHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    ManagedPersonLoader loader,
    LifecycleSettingsStore settings,
    PersonDetails details,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<PersonDetailDto>> HandleAsync(ExtendSponsorshipCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var loaded = await loader.LoadAsync(command.PersonId, ct);
        if (loaded.IsFailure)
        {
            return loaded.Error;
        }

        var person = loaded.Value;
        var previousEnd = person.Eligibility.End;
        var changed = person.ChangeEligibilityEnd(command.End, await settings.GetAsync(ct), time.GetUtcNow());
        if (changed.IsFailure)
        {
            return changed.Error;
        }

        var me = await currentUser.RequirePersonIdAsync(ct);
        audit.Record(AuditActor.ForPerson(me), "person.eligibility_changed", "Person", person.Id.Value.ToString(),
            new { start = person.Eligibility.Start, end = person.Eligibility.End, previousEnd });
        await db.SaveChangesAsync(ct);
        return await details.BuildAsync(person, ct);
    }
}
