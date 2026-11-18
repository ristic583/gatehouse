using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.GetAccessRequest;

public sealed class GetAccessRequestHandler(
    ITenantDb db,
    IAuthorizationService authorization,
    EntitlementResourceLoader resources,
    AccessRequestDtoBuilder dtos)
{
    public async Task<Result<AccessRequestDto>> HandleAsync(GetAccessRequestQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = new EntitlementId(query.EntitlementId);
        var entitlement = await db.Entitlements.AsNoTracking().SingleOrDefaultAsync(e => e.Id == id, ct);
        if (entitlement is null)
        {
            return NotFound();
        }

        var resource = await resources.LoadAsync(entitlement, ct);
        var allowed = await authorization.AuthorizeAsync(new ClaimsPrincipal(), resource, new ViewEntitlementRequirement());
        if (!allowed.Succeeded)
        {
            return NotFound();
        }

        return (await dtos.BuildAsync([entitlement], ct))[0];
    }

    private static Error NotFound() => Error.NotFound("entitlement.not_found", "Access request not found.");
}
