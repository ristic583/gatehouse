using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.ListCards;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.Sponsorship;

/// <summary>Loads a tracked person the caller may see (<c>404 person.not_found</c>) and manage (<c>403 auth.forbidden</c>).</summary>
public sealed class ManagedPersonLoader(ITenantDb db, ICurrentUser currentUser, CardAccess access)
{
    public async Task<Result<Person>> LoadAsync(PersonId personId, CancellationToken ct)
    {
        var person = await db.Persons.VisibleTo(currentUser).FirstOrDefaultAsync(p => p.Id == personId, ct);
        if (person is null)
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        if (!await access.AllowedAsync(CardAccess.ResourceFor(person), new ManagePersonRequirement()))
        {
            return Error.Forbidden("auth.forbidden", "You are not allowed to manage this person.");
        }

        return person;
    }
}
