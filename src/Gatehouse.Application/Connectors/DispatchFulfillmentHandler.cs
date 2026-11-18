using System.Text.Json;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.Tenancy;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Credentials;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;
using AccessRevocationKind = Gatehouse.Domain.Access.RevocationKind;
using AccessWindow = Gatehouse.Domain.Access.AccessWindow;
using Entitlement = Gatehouse.Domain.Access.Entitlement;
using EntitlementStatus = Gatehouse.Domain.Access.EntitlementStatus;

namespace Gatehouse.Application.Connectors;

/// <summary>Consumer logic for <c>FulfillmentDesired</c>: turns a Connector-mode fulfillment into ordered commands; the consumer
/// saves.</summary>
public sealed class DispatchFulfillmentHandler(
    ITenantDb db,
    ConnectorCommandFactory factory,
    IConnectorCommandSignal signal,
    IAuditLog audit,
    ITenantContext tenant,
    TimeProvider time,
    GrantMigrationService migration)
{
    private static readonly AuditActor Dispatcher = AuditActor.ForSystem("dispatcher");

    public async Task<Result> HandleAsync(DispatchFulfillmentCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var fulfillment = await db.Fulfillments.FirstOrDefaultAsync(f => f.Id == command.FulfillmentId, ct);
        if (fulfillment is null || fulfillment.Status != FulfillmentStatus.Desired)
        {
            return Result.Success();
        }

        var instance = await db.PacsInstances.AsNoTracking().FirstAsync(p => p.Id == fulfillment.PacsInstanceId, ct);
        if (instance.FulfillmentMode != FulfillmentMode.Connector)
        {
            return Result.Success();
        }

        var now = time.GetUtcNow();
        var entitlement = await db.Entitlements.AsNoTracking().FirstAsync(e => e.Id == fulfillment.EntitlementId, ct);
        var person = await db.Persons.AsNoTracking().FirstAsync(p => p.Id == entitlement.PersonId, ct);
        var cards = await db.Cards.AsNoTracking().Where(c => c.PersonId == person.Id && c.State != CardState.Revoked).ToListAsync(ct);
        var card = cards.FirstOrDefault(c => c.State == CardState.Active)
            ?? cards.OrderByDescending(c => c.StateChangedAt).FirstOrDefault();
        if (card is null && fulfillment.Kind == FulfillmentKind.Revoke)
        {
            // Termination revokes the card in the same save; the PACS still knows the access by that (now revoked) card number.
            card = await db.Cards.AsNoTracking()
                .Where(c => c.PersonId == person.Id)
                .OrderByDescending(c => c.StateChangedAt)
                .FirstOrDefaultAsync(ct);
        }

        if (card is null)
        {
            fulfillment.Fail(Fulfillment.FulfillmentPlanner.NoActiveCardCode, "The person has no card to provision.", now);
            audit.Record(
                Dispatcher, "fulfillment.failed", AuditSubjects.Fulfillment, fulfillment.Id.Value.ToString(),
                new { code = "fulfillment.no_active_card" });
            return Result.Success();
        }

        // A revoke removes the level that was granted, not whatever the area maps to now (GH-ACCESS-01); a migration revoke (entitlement
        // still approved) removes the level recorded when the mapping edit planned it, also when retried after the new level went out.
        string? accessLevel = null;
        if (fulfillment.Kind == FulfillmentKind.Revoke)
        {
            var sources = entitlement.Status == EntitlementStatus.Approved
                ? await migration.MigrationSourcesAsync([fulfillment.Id], ct)
                : [];
            accessLevel = sources.TryGetValue(fulfillment.Id, out var source)
                ? source.Level
                : await GrantedLevelAsync(entitlement.Id, fulfillment.PacsInstanceId, ct);
        }

        accessLevel ??= await db.AccessLevelMappings.AsNoTracking()
            .Where(m => m.AreaId == entitlement.AreaId && m.PacsInstanceId == fulfillment.PacsInstanceId)
            .Select(m => m.VendorAccessLevelRef)
            .FirstOrDefaultAsync(ct);
        if (accessLevel is null)
        {
            fulfillment.Fail(NoAccessLevelCode, "The area is not mapped to an access level on this PACS.", now);
            audit.Record(
                Dispatcher, "fulfillment.failed", AuditSubjects.Fulfillment, fulfillment.Id.Value.ToString(),
                new { code = NoAccessLevelCode });
            return Result.Success();
        }

        var personRef = person.Id.Value.ToString();
        var priority = fulfillment.Priority;
        var nativeValidity = PacsCapabilityGaps.From(instance.CapabilitiesJson) is { } gaps
            && !gaps.Contains(PacsCapabilityGaps.NativeValidity);
        ConnectorCommandLast last;
        var live = await LiveEntitlementsAsync(entitlement, fulfillment.PacsInstanceId, accessLevel, now, ct);
        var others = live.Select(l => l.Window).ToList();
        if (fulfillment.Kind == FulfillmentKind.Grant)
        {
            if (entitlement.Status != EntitlementStatus.Approved || entitlement.EffectiveEnd <= now)
            {
                // Approved for a window that is already over (or no longer approved): nothing to grant (GH-ACCESS-02).
                fulfillment.Supersede(now);
                audit.Record(
                    Dispatcher, "fulfillment.superseded", AuditSubjects.Fulfillment, fulfillment.Id.Value.ToString(),
                    new { entitlementId = entitlement.Id.Value, reason = "window_ended" });
                return Result.Success();
            }

            // One assignment per person and level in the PACS: write the merged window so an early renewal never shortens current access.
            var window = AccessWindow.Current([.. others, new AccessWindow(entitlement.RequestedStart, entitlement.EffectiveEnd)])
                ?? new AccessWindow(entitlement.RequestedStart, entitlement.EffectiveEnd);
            var upsert = await factory.CreateAsync(fulfillment, person.Id, ConnectorCommandTypes.UpsertCardholder,
                new UpsertCardholder(personRef, person.GivenName, person.FamilyName, card.CardNumber, null), null, priority, ct);
            var grant = await factory.CreateAsync(fulfillment, person.Id, ConnectorCommandTypes.GrantAccess,
                new GrantAccess(personRef, card.CardNumber, accessLevel, window.Start, window.End), fulfillment.Id, priority, ct);
            DeferUntilStart(grant, window, nativeValidity, now);
            db.ConnectorCommands.AddRange(upsert, grant);
            last = new ConnectorCommandLast(grant.Id);
        }
        else if (AccessWindow.Current(others) is { } remaining && (nativeValidity || remaining.Start <= now))
        {
            // Another live entitlement shares this access level: keep its window instead of removing the assignment.
            var regrant = await factory.CreateAsync(fulfillment, person.Id, ConnectorCommandTypes.GrantAccess,
                new GrantAccess(personRef, card.CardNumber, accessLevel, remaining.Start, remaining.End), fulfillment.Id, priority, ct);
            db.ConnectorCommands.Add(regrant);
            await SupersedeQueuedGrantsAsync(fulfillment, now, ct);
            last = new ConnectorCommandLast(regrant.Id);
        }
        else
        {
            var kind = entitlement.Revocation?.Kind == AccessRevocationKind.Emergency ? RevocationKind.Emergency
                : entitlement.Revocation?.Kind == AccessRevocationKind.Termination ? RevocationKind.Termination : RevocationKind.Normal;
            var revoke = await factory.CreateAsync(fulfillment, person.Id, ConnectorCommandTypes.RevokeAccess,
                new RevokeAccess(personRef, card.CardNumber, accessLevel, kind), fulfillment.Id, priority, ct);
            db.ConnectorCommands.Add(revoke);
            if (AccessWindow.Current(others) is not null)
            {
                // The remaining window starts later and this PACS cannot hold a future window: remove now; the remainder is granted
                // at its start by its own Grant fulfillment (held by DeferUntil when dispatched), which fails like any other (GH-CONN-01).
                await RegrantRemainderAsync(fulfillment, live.MinBy(l => l.Window.Start).Id, now, ct);
            }

            await SupersedeQueuedGrantsAsync(fulfillment, now, ct);
            last = new ConnectorCommandLast(revoke.Id);
        }

        if (fulfillment.Kind == FulfillmentKind.Revoke)
        {
            // Mapping edit (GH-ACCESS-01): the grant is re-applied with the new level only now, after this revoke command exists, so its
            // commands are sequenced after it and the queued-grant supersession above cannot touch them.
            await migration.PlanFollowUpGrantAsync(fulfillment, Dispatcher, now, ct);
        }

        fulfillment.MarkPending(last.Id, now);
        await signal.NotifyAsync(tenant.TenantId, fulfillment.PacsInstanceId, ct);
        return Result.Success();
    }

    /// <summary>Error code of a fulfillment whose area has no access level on the PACS (mapping deleted, nothing granted before).</summary>
    public const string NoAccessLevelCode = "fulfillment.no_access_level";

    /// <summary>
    /// One grant per entitlement and instance (unique): the remainder's own grant, usually already applied with the merged window, is
    /// re-applied (its window alone, deferred); a desired one goes out on its own, a failed one stays with its task.
    /// </summary>
    private async Task RegrantRemainderAsync(
        Domain.Fulfillment.Fulfillment revoke, Domain.Access.EntitlementId remainderId, DateTimeOffset now, CancellationToken ct)
    {
        var existing = await db.Fulfillments.FirstOrDefaultAsync(
            f => f.EntitlementId == remainderId && f.PacsInstanceId == revoke.PacsInstanceId && f.Kind == FulfillmentKind.Grant, ct);
        if (existing is not null)
        {
            existing.Reapply(CommandPriority.Normal, now);
        }
        else if (revoke.PlanDeferredRegrant(remainderId, now) is { IsSuccess: true } regrant)
        {
            db.Fulfillments.Add(regrant.Value);
        }
    }

    /// <summary>A PACS without native validity would open access at once: hold the grant until its window starts (GH-CONN-01).</summary>
    private static void DeferUntilStart(ConnectorCommand grant, AccessWindow window, bool nativeValidity, DateTimeOffset now)
    {
        if (!nativeValidity && window.Start > now)
        {
            grant.DeferUntil(window.Start);
        }
    }

    /// <summary>The level of the latest grant command sent for this entitlement on this PACS (not superseded), or null.</summary>
    private async Task<string?> GrantedLevelAsync(
        Domain.Access.EntitlementId entitlementId, PacsInstanceId instanceId, CancellationToken ct)
    {
        var payload = await (from c in db.ConnectorCommands.AsNoTracking()
                             join f in db.Fulfillments.AsNoTracking() on c.FulfillmentId equals (FulfillmentId?)f.Id
                             where f.EntitlementId == entitlementId && f.Kind == FulfillmentKind.Grant && c.PacsInstanceId == instanceId
                                 && c.Type == ConnectorCommandTypes.GrantAccess && c.Status != ConnectorCommandStatus.Superseded
                             orderby c.CreatedAt descending
                             select c.PayloadJson).FirstOrDefaultAsync(ct);
        return payload is null ? null : JsonSerializer.Deserialize<GrantAccess>(payload, ConnectorJson.Options)?.VendorAccessLevelRef;
    }

    /// <summary>Approved entitlements (not yet ended) of the same person for the same access level on this PACS, other than this
    /// one.</summary>
    private async Task<List<(Domain.Access.EntitlementId Id, AccessWindow Window)>> LiveEntitlementsAsync(
        Entitlement entitlement, PacsInstanceId instanceId, string accessLevel, DateTimeOffset now, CancellationToken ct)
    {
        var rows = await (from e in db.Entitlements.AsNoTracking()
                          join m in db.AccessLevelMappings.AsNoTracking() on e.AreaId equals m.AreaId
                          where e.PersonId == entitlement.PersonId && e.Id != entitlement.Id
                              && e.Status == EntitlementStatus.Approved && e.EffectiveEnd > now
                              && m.PacsInstanceId == instanceId && m.VendorAccessLevelRef == accessLevel
                          select new { e.Id, e.RequestedStart, e.EffectiveEnd })
            .ToListAsync(ct);
        return rows.Select(r => (r.Id, new AccessWindow(r.RequestedStart, r.EffectiveEnd))).ToList();
    }

    private async Task SupersedeQueuedGrantsAsync(Domain.Fulfillment.Fulfillment fulfillment, DateTimeOffset now, CancellationToken ct)
    {
        var grantIds = await db.Fulfillments.AsNoTracking()
            .Where(f => f.EntitlementId == fulfillment.EntitlementId && f.Kind == FulfillmentKind.Grant)
            .Select(f => f.Id).ToListAsync(ct);
        var queued = await db.ConnectorCommands
            .Where(c => c.PacsInstanceId == fulfillment.PacsInstanceId && c.Status == ConnectorCommandStatus.Queued
            && c.FulfillmentId != null)
            .ToListAsync(ct);
        foreach (var stale in queued.Where(c => grantIds.Contains(c.FulfillmentId!.Value)))
        {
            stale.Supersede(now);
        }
    }

    private readonly record struct ConnectorCommandLast(CommandId Id);
}
