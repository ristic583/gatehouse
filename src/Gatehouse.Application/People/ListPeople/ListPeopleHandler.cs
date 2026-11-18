using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.People.ListPeople;

/// <summary><c>GET /api/v1/people</c> through <c>VisibleTo(me)</c>, sorted by family then given name; limit 50 by default, 200 at
/// most.</summary>
public sealed class ListPeopleHandler(ITenantDb db, ICurrentUser currentUser, PersonSummaries summaries)
{
    public const int DefaultLimit = 50;
    public const int MaxLimit = 200;

    public async Task<Result<IReadOnlyList<PersonSummaryDto>>> HandleAsync(ListPeopleQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var people = db.Persons.AsNoTracking().VisibleTo(currentUser);
        if (!string.IsNullOrWhiteSpace(query.Type))
        {
            if (!Enum.TryParse<PersonType>(query.Type, ignoreCase: true, out var type))
            {
                return Result.Success<IReadOnlyList<PersonSummaryDto>>([]);
            }

            people = people.Where(p => p.Type == type);
        }

        if (!string.IsNullOrWhiteSpace(query.Status))
        {
            if (!Enum.TryParse<PersonStatus>(query.Status, ignoreCase: true, out var status))
            {
                return Result.Success<IReadOnlyList<PersonSummaryDto>>([]);
            }

            people = people.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Query))
        {
            var term = query.Query.Trim().ToLowerInvariant();
#pragma warning disable CA1862, CA1304, CA1311 // EF translates ToLower() to SQL lower(); the culture/comparison overloads do not
            people = people.Where(p => p.Email.Contains(term) || p.GivenName.ToLower().Contains(term)
                || p.FamilyName.ToLower().Contains(term));
#pragma warning restore CA1862, CA1304, CA1311
        }

        var limit = Math.Clamp(query.Limit ?? DefaultLimit, 1, MaxLimit);
        var page = await people.OrderBy(p => p.FamilyName).ThenBy(p => p.GivenName).Take(limit).ToListAsync(ct);
        return Result.Success(await summaries.ToDtosAsync(page, ct));
    }
}
