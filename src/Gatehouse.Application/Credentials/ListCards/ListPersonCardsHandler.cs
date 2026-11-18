using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ListCards;

/// <summary><c>GET /api/v1/people/{personId}/cards</c>: self or <c>ManagePersonRequirement</c>; otherwise 404.</summary>
public sealed class ListPersonCardsHandler(ITenantDb db, CardAccess access, CardReadModel readModel)
{
    public async Task<Result<IReadOnlyList<CardDto>>> HandleAsync(ListPersonCardsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var person = await access.FindPersonAsync(query.PersonId, ct);
        if (person is null || !await access.CanSeeAsync(CardAccess.ResourceFor(person)))
        {
            return Error.NotFound("person.not_found", "Person not found.");
        }

        var cards = await db.Cards.AsNoTracking()
            .Where(c => c.PersonId == query.PersonId)
            .OrderByDescending(c => c.StateChangedAt)
            .ToListAsync(ct);
        return Result.Success(await readModel.ToDtosAsync(cards, ct));
    }
}
