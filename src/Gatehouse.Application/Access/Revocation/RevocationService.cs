using System.Security.Claims;
using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Access.GetAccessRequest;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.Revocation;

/// <summary>Shared flow of owner revoke and emergency revoke.</summary>
public sealed class RevocationService(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    EntitlementResourceLoader resources,
    FulfillmentPlanner planner,
    AccessRequestDtoBuilder dtos,
    IAuditLog audit,
    TimeProvider time)
{
    public async Task<Result<AccessRequestDto>> RevokeAsync(RevokeEntitlementCommand command, bool emergency, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var id = new EntitlementId(command.EntitlementId);
        var entitlement = await db.Entitlements.SingleOrDefaultAsync(e => e.Id == id, ct);
        if (entitlement is null)
        {
            return NotFound();
        }

        var resource = await resources.LoadAsync(entitlement, ct);
        var principal = new ClaimsPrincipal();
        if (!(await authorization.AuthorizeAsync(principal, resource, new ViewEntitlementRequirement())).Succeeded)
        {
            return NotFound();
        }

        if (emergency)
        {
            if (!(await authorization.AuthorizeAsync(principal, resource, new EmergencyRevokeRequirement())).Succeeded)
            {
                return Error.Forbidden("auth.forbidden", "You are not allowed to emergency-revoke this access.");
            }
        }
        else if (!(await authorization.AuthorizeAsync(principal, resource, new RevokeEntitlementRequirement())).Succeeded)
        {
            return Error.Forbidden("entitlement.revoke_forbidden", "Only an owner of the area can revoke this access.");
        }

        var kind = emergency ? RevocationKind.Emergency : RevocationKind.Normal;
        var revoked = entitlement.Revoke(me, command.Reason ?? string.Empty, kind, now);
        if (revoked.IsFailure)
        {
            return revoked.Error;
        }

        var actor = AuditActor.ForPerson(me);
        var revokeIds = await planner.PlanRevocationAsync(
            entitlement, emergency ? CommandPriority.High : CommandPriority.Normal, actor, now, ct);
        audit.Record(
            actor,
            "entitlement.revoked",
            AuditSubjects.Entitlement,
            entitlement.Id.Value.ToString(),
            new { kind = kind.ToString(), reason = command.Reason, revokeFulfillmentIds = revokeIds.Select(r => r.Value).ToList() });

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

    private static Error NotFound() => Error.NotFound("entitlement.not_found", "Access request not found.");
}
