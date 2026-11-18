using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.ListPeople;

/// <summary>Builds <see cref="PersonSummaryDto"/>s with sponsor and manager names.</summary>
public sealed class PersonSummaries(ITenantDb db)
{
    public async Task<IReadOnlyList<PersonSummaryDto>> ToDtosAsync(IReadOnlyList<Person> people, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(people);
        var refIds = people.SelectMany(p => new[] { p.SponsorId, p.ManagerId }).OfType<PersonId>().Distinct().ToList();
        var refs = refIds.Count == 0
            ? new Dictionary<PersonId, PersonRefDto>()
            : await db.Persons.AsNoTracking().Where(p => refIds.Contains(p.Id))
                .Select(p => new { p.Id, Ref = new PersonRefDto(p.Id.Value, p.GivenName + " " + p.FamilyName) })
                .ToDictionaryAsync(p => p.Id, p => p.Ref, ct);

        return people.Select(p => new PersonSummaryDto(
            p.Id.Value,
            p.DisplayName,
            p.Email,
            p.Type.ToString(),
            p.Source.ToString(),
            p.Status.ToString(),
            p.Eligibility.Start,
            p.Eligibility.End,
            p.SponsorId is { } s ? refs.GetValueOrDefault(s) : null,
            p.ManagerId is { } m ? refs.GetValueOrDefault(m) : null)).ToList();
    }
}
