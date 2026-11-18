using System.Security.Claims;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Authorization.Handlers;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Authorization.Resources;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Fulfillment;

/// <summary>Shared flow of the manual fulfillment actions: load in scope, authorize, act, audit, save once.</summary>
public sealed class FulfillmentActionService(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    IAuditLog audit,
    FulfillmentDetailLoader loader,
    TimeProvider time)
{
    public async Task<Result<FulfillmentDetailDto>> RunAsync(
        Guid fulfillmentId,
        Func<FulfillmentEntity, DateTimeOffset, Result> act,
        string auditAction,
        Func<FulfillmentActionContext, Task>? after,
        CancellationToken ct,
        bool staffManaged = true)
    {
        ArgumentNullException.ThrowIfNull(act);
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var id = new FulfillmentId(fulfillmentId);

        var fulfillment = await db.Fulfillments
            .HandledBy(currentUser, db.Entitlements, db.Areas, db.Floors)
            .SingleOrDefaultAsync(f => f.Id == id, ct);
        if (fulfillment is null)
        {
            return NotFound();
        }

        var info = await db.Entitlements.AsNoTracking()
            .Where(e => e.Id == fulfillment.EntitlementId)
            .Join(db.Areas, e => e.AreaId, a => a.Id, (e, a) => new { e.PersonId, e.RequestedById, a.FloorId })
            .Join(db.Floors, x => x.FloorId, fl => fl.Id, (x, fl) => new { x.PersonId, x.RequestedById, fl.SiteId })
            .SingleOrDefaultAsync(ct);
        if (info is null)
        {
            return NotFound();
        }

        var allowed = await authorization.AuthorizeAsync(
            new ClaimsPrincipal(),
            new FulfillmentResource(fulfillment.Id, info.SiteId, info.PersonId, info.RequestedById),
            new HandleFulfillmentRequirement());
        if (!allowed.Succeeded)
        {
            return allowed.Failure?.FailureReasons.Any(r => r.Handler is NoSelfFulfillmentHandler) == true
                ? Error.Forbidden("fulfillment.self_handling_forbidden", "You cannot handle a fulfillment of your own request.")
                : NotFound();
        }

        if (staffManaged
            && await db.PacsInstances.AsNoTracking()
            .AnyAsync(p => p.Id == fulfillment.PacsInstanceId && p.FulfillmentMode == FulfillmentMode.Connector, ct))
        {
            return Error.Conflict("fulfillment.connector_managed", "This fulfillment is handled by the connector agent.");
        }

        var result = act(fulfillment, now);
        if (result.IsFailure)
        {
            return result.Error;
        }

        var actor = AuditActor.ForPerson(me);
        if (after is not null)
        {
            await after(new FulfillmentActionContext(fulfillment, info.SiteId, me, actor, now));
        }

        audit.Record(
            actor,
            auditAction,
            AuditSubjects.Fulfillment,
            fulfillment.Id.Value.ToString(),
            AuditPayload(auditAction, fulfillment, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Error.Conflict("fulfillment.invalid_transition", "The fulfillment was changed by someone else.");
        }

        return await loader.LoadAsync(fulfillment, ct);
    }

    private static Error NotFound() => Error.NotFound("fulfillment.not_found", "Fulfillment not found.");

    private static object AuditPayload(string action, FulfillmentEntity f, DateTimeOffset now) => action switch
    {
        "fulfillment.confirmed" => new
        {
            entitlementId = f.EntitlementId.Value,
            kind = f.Kind.ToString(),
            revision = f.Revision,
            confirmedAt = f.ConfirmedAt?.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
                ?? now.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        },
        "fulfillment.failed" => new
        {
            entitlementId = f.EntitlementId.Value,
            kind = f.Kind.ToString(),
            revision = f.Revision,
            errorCode = f.LastError?.Split(": ", 2)[0],
            reason = f.LastError?.Split(": ", 2).ElementAtOrDefault(1),
        },
        _ => new { entitlementId = f.EntitlementId.Value, kind = f.Kind.ToString(), revision = f.Revision },
    };
}

public sealed record FulfillmentActionContext(
    FulfillmentEntity Fulfillment, SiteId SiteId, PersonId Me, AuditActor Actor, DateTimeOffset Now);
