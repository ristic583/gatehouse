using System.Security.Claims;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Authorization.Requirements;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Identity;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.LiveBuilding;
using Gatehouse.Domain.Sites;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using LockdownAction = Gatehouse.Connector.Contracts.Adapters.LockdownAction;

namespace Gatehouse.Application.LiveBuilding.RequestDoorCommand;

/// <summary>Momentary unlock / lockdown: one High priority connector command per connector-managed door in scope (authorization.md
/// §9).</summary>
public sealed class RequestDoorCommandHandler(
    ITenantDb db,
    ICurrentUser currentUser,
    IAuthorizationService authorization,
    ConnectorCommandFactory factory,
    IConnectorCommandSignal signal,
    IAuditLog audit,
    ITenantContext tenant,
    TimeProvider time)
{
    public const string AuditSubject = "DoorCommand";

    /// <summary>A momentary unlock that has not executed within this time is dropped by gateway and agent (GH-LIVE-01).</summary>
    public static readonly TimeSpan UnlockDeadline = MomentaryUnlock.DefaultDeadline;

    public async Task<Result<DoorCommandResultDto>> HandleAsync(RequestDoorCommandCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var siteId = await ResolveSiteAsync(command.Scope, command.ScopeId, ct);
        if (siteId is not { } site
            || !(await authorization.AuthorizeAsync(new ClaimsPrincipal(), site, new OperateDoorsRequirement())).Succeeded)
        {
            return Error.NotFound("door_command.scope_not_found", "Door command scope not found.");
        }

        var doors = await DoorsInScopeAsync(command.Scope, command.ScopeId, ct);
        var instances = await ConnectorDoorResolver.ResolveAsync(db, site, doors, ct);
        if (instances.Count == 0)
        {
            return Error.Conflict("door_command.no_connector_doors", "No door in scope is handled by a connector.");
        }

        var needed = command.Kind == DoorCommandKind.MomentaryUnlock
            ? Gatehouse.Connector.Contracts.Adapters.DoorCommandKind.MomentaryUnlock
            : Gatehouse.Connector.Contracts.Adapters.DoorCommandKind.Lockdown;
        var connectorDoors = doors
            .Where(d => instances.TryGetValue(d.Id, out var i) && PacsCapabilityGaps.Supports(i.CapabilitiesJson, needed))
            .ToList();
        if (connectorDoors.Count == 0)
        {
            return Error.Conflict("door_command.unsupported_by_pacs", "No PACS handling the doors in scope supports this command.");
        }

        var requestedBy = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var requested = DoorCommand.Request(
            command.Kind, command.Scope, command.ScopeId, requestedBy, command.Reason, command.DurationSeconds, connectorDoors.Count, now);
        if (requested.IsFailure)
        {
            return requested.Error;
        }

        var doorCommand = requested.Value;
        db.DoorCommands.Add(doorCommand);
        foreach (var door in connectorDoors)
        {
            string[] refs = [door.PacsDoorRef!];
            (string type, object payload) = doorCommand.Kind switch
            {
                DoorCommandKind.MomentaryUnlock =>
                    (
                        ConnectorCommandTypes.MomentaryUnlock,
                        (object)new MomentaryUnlock(refs, doorCommand.DurationSeconds!.Value, doorCommand.Reason, now + UnlockDeadline)),
                DoorCommandKind.LockdownStart =>
                    (ConnectorCommandTypes.Lockdown, new Lockdown(refs, LockdownAction.Start, doorCommand.Reason)),
                _ => (ConnectorCommandTypes.Lockdown, new Lockdown(refs, LockdownAction.Clear, doorCommand.Reason)),
            };
            db.ConnectorCommands.Add(await factory.CreateForDoorAsync(instances[door.Id].Id, doorCommand.Id, door.Id, type, payload, ct));
        }

        audit.Record(AuditActor.ForPerson(requestedBy), "door.command_requested", AuditSubject, doorCommand.Id.Value.ToString(), new
        {
            kind = doorCommand.Kind.ToString(),
            scope = doorCommand.Scope.ToString(),
            scopeId = doorCommand.ScopeId,
            reason = doorCommand.Reason,
            durationSeconds = doorCommand.DurationSeconds,
            doorCount = doorCommand.DoorCount,
        });
        await db.SaveChangesAsync(ct);
        foreach (var instanceId in connectorDoors.Select(d => instances[d.Id].Id).Distinct())
        {
            await signal.NotifyAsync(tenant.TenantId, instanceId, ct);
        }

        return new DoorCommandResultDto(doorCommand.Id.Value, connectorDoors.Count, doors.Count - connectorDoors.Count);
    }

    private async Task<SiteId?> ResolveSiteAsync(DoorCommandScope scope, Guid scopeId, CancellationToken ct)
    {
        IQueryable<SiteId> query = scope switch
        {
            DoorCommandScope.Door =>
                from d in db.Doors
                join a in db.Areas on d.AreaId equals a.Id
                join f in db.Floors on a.FloorId equals f.Id
                where d.Id == new DoorId(scopeId)
                select f.SiteId,
            DoorCommandScope.Area =>
                from a in db.Areas
                join f in db.Floors on a.FloorId equals f.Id
                where a.Id == new AreaId(scopeId)
                select f.SiteId,
            DoorCommandScope.Floor => db.Floors.Where(f => f.Id == new FloorId(scopeId)).Select(f => f.SiteId),
            _ => db.Sites.Where(s => s.Id == new SiteId(scopeId)).Select(s => s.Id),
        };
        var ids = await query.Take(1).ToListAsync(ct);
        return ids.Count == 0 ? null : ids[0];
    }

    private async Task<List<Door>> DoorsInScopeAsync(DoorCommandScope scope, Guid scopeId, CancellationToken ct)
    {
        var rows =
            from d in db.Doors.AsNoTracking()
            join a in db.Areas on d.AreaId equals a.Id
            join f in db.Floors on a.FloorId equals f.Id
            select new { Door = d, AreaId = a.Id, FloorId = f.Id, f.SiteId };
        rows = scope switch
        {
            DoorCommandScope.Door => rows.Where(r => r.Door.Id == new DoorId(scopeId)),
            DoorCommandScope.Area => rows.Where(r => r.AreaId == new AreaId(scopeId)),
            DoorCommandScope.Floor => rows.Where(r => r.FloorId == new FloorId(scopeId)),
            _ => rows.Where(r => r.SiteId == new SiteId(scopeId)),
        };
        return await rows.OrderBy(r => r.Door.Name).Select(r => r.Door).ToListAsync(ct);
    }
}
