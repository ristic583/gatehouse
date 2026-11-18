using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Fulfillment.PacsAdmin;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;
using FulfillmentEntity = Gatehouse.Domain.Fulfillment.Fulfillment;

namespace Gatehouse.Application.Fulfillment;

/// <summary>Builds fulfillment queue items and desired states with a fixed number of queries.</summary>
public sealed class FulfillmentDtoBuilder(ITenantDb db, GrantMigrationService migration)
{
    public const string Unmapped = "(unmapped)";

    public async Task<IReadOnlyList<(FulfillmentQueueItemDto Item, DesiredStateDto Desired)>> BuildAsync(
        IReadOnlyList<FulfillmentEntity> fulfillments,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(fulfillments);
        if (fulfillments.Count == 0)
        {
            return [];
        }

        var entitlementIds = fulfillments.Select(f => f.EntitlementId).Distinct().ToList();
        var entitlements = await db.Entitlements.AsNoTracking()
            .Where(e => entitlementIds.Contains(e.Id))
            .Select(e => new { e.Id, e.PersonId, e.AreaId, e.RequestedStart, e.EffectiveEnd, e.Status })
            .ToDictionaryAsync(e => e.Id, ct);

        var personIds = entitlements.Values.Select(e => e.PersonId).Distinct().ToList();
        var people = await db.Persons.AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.GivenName + " " + p.FamilyName })
            .ToDictionaryAsync(p => p.Id, p => new PersonRefDto(p.Id.Value, p.Name), ct);

        var areaIds = entitlements.Values.Select(e => e.AreaId).Distinct().ToList();
        var areas = await db.Areas.AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .Join(db.Floors, a => a.FloorId, f => f.Id, (a, f) => new { a.Id, a.Name, FloorName = f.Name, f.SiteId })
            .Join(db.Sites, x => x.SiteId, s => s.Id, (x, s) => new { x.Id, x.Name, x.FloorName, SiteName = s.Name })
            .ToDictionaryAsync(a => a.Id, a => new AreaRefDto(a.Id.Value, a.Name, a.SiteName, a.FloorName), ct);

        var instanceIds = fulfillments.Select(f => f.PacsInstanceId).Distinct().ToList();
        var instances = await db.PacsInstances.AsNoTracking()
            .Where(i => instanceIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, ct);

        var mappingRows = await db.AccessLevelMappings.AsNoTracking()
            .Where(m => areaIds.Contains(m.AreaId) && instanceIds.Contains(m.PacsInstanceId))
            .ToListAsync(ct);
        var mappings = mappingRows.ToDictionary(m => (m.AreaId, m.PacsInstanceId));
        var migrationRevokes = fulfillments
            .Where(f => f.Kind == Domain.Fulfillment.FulfillmentKind.Revoke
                && entitlements[f.EntitlementId].Status == Domain.Access.EntitlementStatus.Approved)
            .Select(f => f.Id).ToList();
        var migrated = await migration.MigrationSourcesAsync(migrationRevokes, ct);

        return fulfillments.Select(f =>
        {
            var e = entitlements[f.EntitlementId];
            var person = people[e.PersonId];
            var instance = instances[f.PacsInstanceId];
            mappings.TryGetValue((e.AreaId, f.PacsInstanceId), out var mapping);
            // A migration revoke removes the level of the old mapping, not the current one (GH-ACCESS-01; staff act on it for manual PACS).
            var from = migrated.TryGetValue(f.Id, out var m) ? m : ((string Level, string DisplayName)?)null;
            var levelRef = from?.Level ?? mapping?.VendorAccessLevelRef ?? Unmapped;
            var item = new FulfillmentQueueItemDto(
                f.Id.Value,
                f.EntitlementId.Value,
                f.Kind.ToString(),
                f.Status.ToString(),
                f.Priority.ToString(),
                f.Revision,
                person,
                areas[e.AreaId],
                instance.Name,
                levelRef,
                e.Status.ToString(),
                f.LastError,
                f.CreatedAt,
                f.UpdatedAt);
            var desired = new DesiredStateDto(
                f.Kind.ToString(),
                person.PersonId.ToString(),
                person.DisplayName,
                levelRef,
                from?.DisplayName ?? mapping?.DisplayName ?? Unmapped,
                e.RequestedStart,
                e.EffectiveEnd,
                instance.Name,
                instance.Vendor.ToString());
            return (item, desired);
        }).ToList();
    }
}
