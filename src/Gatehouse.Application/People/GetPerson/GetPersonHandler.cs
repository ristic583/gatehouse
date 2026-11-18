using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.GetPerson;

/// <summary><c>GET /api/v1/people/{id}</c>: visible through <c>VisibleTo(me)</c>, otherwise <c>404 person.not_found</c>.</summary>
public sealed class GetPersonHandler(ITenantDb db, ICurrentUser currentUser, PersonDetails details)
{
    public async Task<Result<PersonDetailDto>> HandleAsync(GetPersonQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var person = await db.Persons.AsNoTracking().VisibleTo(currentUser).FirstOrDefaultAsync(p => p.Id == query.PersonId, ct);
        if (person is null)
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        return await details.BuildAsync(person, ct);
    }
}
