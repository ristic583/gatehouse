using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.People.Dtos;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.Dtos;

/// <summary>Builds <see cref="AccessRequestDto"/>s for loaded entitlements with a fixed number of queries.</summary>
public sealed class AccessRequestDtoBuilder(ITenantDb db)
{
    public async Task<IReadOnlyList<AccessRequestDto>> BuildAsync(IReadOnlyList<Entitlement> entitlements, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(entitlements);
        if (entitlements.Count == 0)
        {
            return [];
        }

        var personIds = entitlements
            .SelectMany(e => new PersonId?[] { e.PersonId, e.RequestedById }
                .Concat(e.Steps.SelectMany(s => new[] { s.AssignedApproverId, s.DecidedById })))
            .OfType<PersonId>()
            .Distinct()
            .ToList();
        var people = await db.Persons.AsNoTracking()
            .Where(p => personIds.Contains(p.Id))
            .Select(p => new { p.Id, Name = p.GivenName + " " + p.FamilyName })
            .ToDictionaryAsync(p => p.Id, p => new PersonRefDto(p.Id.Value, p.Name), ct);

        var areaIds = entitlements.Select(e => e.AreaId).Distinct().ToList();
        var areas = await db.Areas.AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .Join(db.Floors, a => a.FloorId, f => f.Id, (a, f) => new { a.Id, a.Name, FloorName = f.Name, f.SiteId })
            .Join(db.Sites, x => x.SiteId, s => s.Id, (x, s) => new { x.Id, x.Name, x.FloorName, SiteName = s.Name })
            .ToDictionaryAsync(a => a.Id, a => new AreaRefDto(a.Id.Value, a.Name, a.SiteName, a.FloorName), ct);

        var entitlementIds = entitlements.Select(e => e.Id).ToList();
        var fulfillments = (await db.Fulfillments.AsNoTracking()
                .Where(f => entitlementIds.Contains(f.EntitlementId))
                .Select(f => new { f.EntitlementId, f.Kind, f.Status })
                .ToListAsync(ct))
            .ToLookup(f => f.EntitlementId, f => (f.Kind, f.Status));

        PersonRefDto? Ref(PersonId? id) => id is { } value && people.TryGetValue(value, out var p) ? p : null;

        return entitlements.Select(e => new AccessRequestDto(
            e.Id.Value,
            Ref(e.PersonId)!,
            Ref(e.RequestedById)!,
            areas[e.AreaId],
            e.RequestedStart,
            e.RequestedEnd,
            e.EffectiveEnd,
            e.CappedToEligibility,
            e.Justification,
            e.Status.ToString(),
            e.Steps.Select(s => new ApprovalStepDto(
                s.Id.Value,
                s.Order,
                s.ApproverKind.ToString(),
                s.Status.ToString(),
                Ref(s.AssignedApproverId),
                Ref(s.DecidedById),
                s.DecidedAt,
                s.Comment)).ToList(),
            SummarizeFulfillment(fulfillments[e.Id].ToList(), FulfillmentKind.Grant),
            e.CreatedAt,
            e.Revocation is { } r ? new RevocationDto(Ref(r.RevokedById), r.RevokedAt, r.Reason, r.Kind.ToString()) : null,
            SummarizeFulfillment(fulfillments[e.Id].ToList(), FulfillmentKind.Revoke))).ToList();
    }

    public static string? SummarizeFulfillment(
        IReadOnlyList<(FulfillmentKind Kind, FulfillmentStatus Status)> fulfillments, FulfillmentKind kind)
    {
        ArgumentNullException.ThrowIfNull(fulfillments);
        var grants = fulfillments.Where(f => f.Kind == kind && f.Status != FulfillmentStatus.Superseded).Select(f => f.Status).ToList();
        if (grants.Count == 0)
        {
            return null;
        }

        if (grants.All(s => s == FulfillmentStatus.Confirmed))
        {
            return nameof(FulfillmentStatus.Confirmed);
        }

        foreach (var status in new[]
            { FulfillmentStatus.Failed, FulfillmentStatus.Desired, FulfillmentStatus.Pending, FulfillmentStatus.Drift })
        {
            if (grants.Contains(status))
            {
                return status.ToString();
            }
        }

        return null;
    }
}
