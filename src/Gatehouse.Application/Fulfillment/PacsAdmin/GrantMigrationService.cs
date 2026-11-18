using System.Text.Json;
using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Commands;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Connectors;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Fulfillment.PacsAdmin;

/// <summary>
/// Moves active grants to a new access level after a mapping edit (GH-ACCESS-01). The edit plans only the <c>Revoke</c> of the granted
/// level; the grant of the new level is re-applied when that revoke goes out: dispatched by the connector, or confirmed by staff on a
/// manual instance. A grant of the new level therefore never precedes the revoke of the old one, and the revoke never supersedes it.
/// There is one fulfillment per entitlement, instance and kind, so existing ones are re-opened (<c>Reapply</c>/<c>Retry</c>) instead
/// of created. The level to remove is recorded in the <see cref="MigrationPlannedAction"/> audit of the revoke. Callers save once.
/// </summary>
public sealed class GrantMigrationService(ITenantDb db, IAuditLog audit)
{
    /// <summary>
    /// Audit on a migration revoke; <c>fromLevel</c>/<c>fromDisplayName</c> is the level it removes (staff see it on manual PACS).
    /// </summary>
    public const string MigrationPlannedAction = "fulfillment.migration_planned";

    /// <summary>Audit on the grant re-applied with the new level once the migration revoke is out.</summary>
    public const string MigrationGrantPlannedAction = "fulfillment.migration_grant_planned";

    /// <summary>
    /// Plans the revoke of the granted level per approved, not ended entitlement on the area with a confirmed grant on the instance.
    /// An entitlement whose revoke is not settled yet (desired or pending) is skipped: its grant is re-applied when that revoke goes out
    /// and then takes the level mapped at that time. Returns the number of planned revokes.
    /// </summary>
    public async Task<int> PlanRevokesAsync(
        PacsInstanceId instanceId,
        AreaId areaId,
        string fromLevel,
        string fromDisplayName,
        string toLevel,
        AuditActor actor,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var granted = await (from e in db.Entitlements.AsNoTracking()
                             join f in db.Fulfillments.AsNoTracking() on e.Id equals f.EntitlementId
                             where e.AreaId == areaId && e.Status == EntitlementStatus.Approved && e.EffectiveEnd > now
                                 && f.PacsInstanceId == instanceId && f.Kind == FulfillmentKind.Grant
                                 && f.Status == FulfillmentStatus.Confirmed
                             select e.Id).Distinct().ToListAsync(ct);
        var revokes = await db.Fulfillments
            .Where(f => granted.Contains(f.EntitlementId) && f.PacsInstanceId == instanceId && f.Kind == FulfillmentKind.Revoke)
            .ToDictionaryAsync(f => f.EntitlementId, ct);
        var planned = 0;
        foreach (var entitlementId in granted)
        {
            var revoke = revokes.GetValueOrDefault(entitlementId);
            if (revoke is null)
            {
                revoke = FulfillmentEntity.Create(entitlementId, instanceId, FulfillmentKind.Revoke, CommandPriority.Normal, now);
                db.Fulfillments.Add(revoke);
            }
            else if (!(revoke.Status == FulfillmentStatus.Confirmed
                ? revoke.Reapply(CommandPriority.Normal, now)
                : revoke.Retry(now)).IsSuccess)
            {
                continue;
            }

            // The level the connector actually granted wins over the previous mapping (they differ only if the mapping changed before).
            var level = await LastGrantedLevelAsync(entitlementId, instanceId, ct) ?? fromLevel;
            audit.Record(actor, MigrationPlannedAction, AuditSubjects.Fulfillment, revoke.Id.Value.ToString(),
                new
                {
                    entitlementId = entitlementId.Value,
                    fromLevel = level,
                    fromDisplayName = level == fromLevel ? fromDisplayName : level,
                    toLevel,
                });
            planned++;
        }

        return planned;
    }

    /// <summary>
    /// After a revoke went out: if its entitlement is still approved and not ended, the revoke was a migration (every other revoke ends
    /// the entitlement first) and the entitlement's grant on that instance is re-applied, taking the level mapped now. A grant that is
    /// still desired takes the current level anyway; a failed one is retried by staff. Returns the re-applied grant or null.
    /// </summary>
    public async Task<FulfillmentEntity?> PlanFollowUpGrantAsync(
        FulfillmentEntity revoke, AuditActor actor, DateTimeOffset now, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(revoke);
        if (revoke.Kind != FulfillmentKind.Revoke
            || !await db.Entitlements.AsNoTracking().AnyAsync(
                e => e.Id == revoke.EntitlementId && e.Status == EntitlementStatus.Approved && e.EffectiveEnd > now, ct))
        {
            return null;
        }

        var grant = await db.Fulfillments.FirstOrDefaultAsync(
            f => f.EntitlementId == revoke.EntitlementId && f.PacsInstanceId == revoke.PacsInstanceId
                && f.Kind == FulfillmentKind.Grant,
            ct);
        if (grant is null || grant.Reapply(CommandPriority.Normal, now).IsFailure)
        {
            return null;
        }

        audit.Record(actor, MigrationGrantPlannedAction, AuditSubjects.Fulfillment, grant.Id.Value.ToString(),
            new { entitlementId = revoke.EntitlementId.Value, revokeFulfillmentId = revoke.Id.Value, revision = grant.Revision });
        return grant;
    }

    /// <summary>
    /// The level each migration revoke removes (latest <see cref="MigrationPlannedAction"/> per revoke), keyed by fulfillment id.
    /// </summary>
    public async Task<Dictionary<FulfillmentId, (string Level, string DisplayName)>> MigrationSourcesAsync(
        IReadOnlyCollection<FulfillmentId> revokeIds, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(revokeIds);
        var subjects = revokeIds.Select(id => id.Value.ToString()).ToList();
        var rows = subjects.Count == 0 ? [] : await db.AuditEntries.AsNoTracking()
            .Where(a => a.Action == MigrationPlannedAction && a.SubjectType == AuditSubjects.Fulfillment && subjects.Contains(a.SubjectId))
            .OrderBy(a => a.Sequence)
            .Select(a => new { a.SubjectId, a.Payload }).ToListAsync(ct);
        var result = new Dictionary<FulfillmentId, (string Level, string DisplayName)>();
        foreach (var row in rows)
        {
            using var payload = JsonDocument.Parse(row.Payload);
            if (payload.RootElement.TryGetProperty("fromLevel", out var level) && level.GetString() is { } levelRef)
            {
                var display = payload.RootElement.TryGetProperty("fromDisplayName", out var name) ? name.GetString() : null;
                result[new FulfillmentId(Guid.Parse(row.SubjectId))] = (levelRef, display ?? levelRef);
            }
        }

        return result;
    }

    private async Task<string?> LastGrantedLevelAsync(EntitlementId entitlementId, PacsInstanceId instanceId, CancellationToken ct)
    {
        var payload = await (from c in db.ConnectorCommands.AsNoTracking()
                             join f in db.Fulfillments.AsNoTracking() on c.FulfillmentId equals (FulfillmentId?)f.Id
                             where f.EntitlementId == entitlementId && f.Kind == FulfillmentKind.Grant && c.PacsInstanceId == instanceId
                                 && c.Type == ConnectorCommandTypes.GrantAccess && c.Status != ConnectorCommandStatus.Superseded
                             orderby c.CreatedAt descending
                             select c.PayloadJson).FirstOrDefaultAsync(ct);
        return payload is null ? null : JsonSerializer.Deserialize<GrantAccess>(payload, ConnectorJson.Options)?.VendorAccessLevelRef;
    }
}
