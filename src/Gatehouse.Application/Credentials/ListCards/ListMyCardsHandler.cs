using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Credentials.Dtos;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Credentials;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.ListCards;

/// <summary><c>GET /api/v1/me/cards</c>: my cards that are not Revoked.</summary>
public sealed class ListMyCardsHandler(ITenantDb db, ICurrentUser currentUser, CardReadModel readModel)
{
    public async Task<Result<IReadOnlyList<CardDto>>> HandleAsync(CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var cards = await db.Cards.AsNoTracking()
            .Where(c => c.PersonId == me && c.State != CardState.Revoked)
            .OrderByDescending(c => c.StateChangedAt)
            .ToListAsync(ct);
        return Result.Success(await readModel.ToDtosAsync(cards, ct));
    }
}
