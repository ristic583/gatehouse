using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.CancelAccessRequest;

public sealed class CancelAccessRequestHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    EntitlementResourceLoader resources,
    AccessRequestDtoBuilder dtos,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<AccessRequestDto>> HandleAsync(CancelAccessRequestCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var id = new EntitlementId(command.EntitlementId);
        var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (entitlement is null
            || !(
                await authorization.AuthorizeAsync(
                    new ClaimsPrincipal(), await resources.LoadAsync(entitlement, ct), new ViewEntitlementRequirement())).Succeeded)
        {
            return Error.NotFound("entitlement.not_found", "Access request not found.");
        }

        var cancelled = entitlement.Cancel(me, time.GetUtcNow());
        if (cancelled.IsFailure)
        {
            return cancelled.Error;
        }

        audit.Record(
            AuditActor.ForPerson(me), "entitlement.cancelled", AuditSubjects.Entitlement, entitlement.Id.Value.ToString(), new { });
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("entitlement.invalid_transition", "The entitlement was changed by someone else.");
        }

        return (await dtos.BuildAsync([entitlement], ct))[0];
    }
}
