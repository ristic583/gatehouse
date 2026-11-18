using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.ListAccessRequests;

/// <summary>My requests: those for me or made by me, newest first.</summary>
public sealed class ListAccessRequestsHandler(ITenantDb db, ICurrentUser currentUser, AccessRequestDtoBuilder dtos)
{
    public async Task<Result<IReadOnlyList<AccessRequestDto>>> HandleAsync(ListAccessRequestsQuery query, CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var entitlements = await db.Entitlements.AsNoTracking()
            .VisibleTo(currentUser, db.Areas, db.Persons, db.Floors)
            .Where(e => e.PersonId == me || e.RequestedById == me)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(ct);
        return Result.Success(await dtos.BuildAsync(entitlements, ct));
    }
}
