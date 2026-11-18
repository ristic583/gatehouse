using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Sites;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.LiveBuilding.LiveDrift;

/// <summary>
/// Live drift rule (domain-model.md §2.7): a granted access is authorized by an <c>Approved</c> entitlement for the door's area
/// with <c>RequestedStart &lt;= OccurredAt &lt; EffectiveEnd</c> whose <c>Grant</c> fulfillments for the instance are all
/// <c>Confirmed</c> or <c>Pending</c>, or by a <c>Revoked</c> entitlement with a <c>Revoke</c> fulfillment not yet <c>Confirmed</c>.
/// </summary>
public sealed class LiveDriftDetector(ITenantDb db)
{
    /// <summary>The ids of the granted events that no entitlement authorizes (an unknown person is never authorized).</summary>
    public async Task<IReadOnlySet<Guid>> FindUnauthorizedAsync(
        IReadOnlyList<(Guid EventId, PersonId? PersonId, AreaId AreaId, DateTimeOffset OccurredAt)> grants,
        PacsInstanceId pacsInstanceId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(grants);
        if (grants.Count == 0)
        {
            return new HashSet<Guid>();
        }

        var personIds = grants.Where(g => g.PersonId is not null).Select(g => g.PersonId!.Value).Distinct().ToList();
        var areaIds = grants.Select(g => g.AreaId).Distinct().ToList();
        var candidates = personIds.Count == 0
            ? []
            : await db.Entitlements.AsNoTracking()
                .Where(e => personIds.Contains(e.PersonId) && areaIds.Contains(e.AreaId)
                    && (e.Status == EntitlementStatus.Approved || e.Status == EntitlementStatus.Revoked))
                .Select(e => new Candidate(
                    e.PersonId,
                    e.AreaId,
                    e.Status,
                    e.RequestedStart,
                    e.EffectiveEnd,
                    db.Fulfillments
                        .Where(f => f.EntitlementId == e.Id && f.PacsInstanceId == pacsInstanceId
                        && f.Status != FulfillmentStatus.Superseded)
                        .Select(f => new FulfillmentState(f.Kind, f.Status)).ToList()))
                .ToListAsync(ct);

        var unauthorized = new HashSet<Guid>();
        foreach (var grant in grants)
        {
            var authorized = grant.PersonId is { } personId
                && candidates.Any(c => c.PersonId == personId && c.AreaId == grant.AreaId && Authorizes(c, grant.OccurredAt));
            if (!authorized)
            {
                unauthorized.Add(grant.EventId);
            }
        }

        return unauthorized;
    }

    private static bool Authorizes(Candidate c, DateTimeOffset occurredAt) => c.Status switch
    {
        EntitlementStatus.Approved => c.RequestedStart <= occurredAt && occurredAt < c.EffectiveEnd
            && c.Fulfillments.Where(f => f.Kind == FulfillmentKind.Grant)
                .All(f => f.Status is FulfillmentStatus.Confirmed or FulfillmentStatus.Pending),
        EntitlementStatus.Revoked => c.Fulfillments.Any(f => f.Kind == FulfillmentKind.Revoke && f.Status != FulfillmentStatus.Confirmed),
        _ => false,
    };

    private sealed record Candidate(
        PersonId PersonId,
        AreaId AreaId,
        EntitlementStatus Status,
        DateTimeOffset RequestedStart,
        DateTimeOffset EffectiveEnd,
        List<FulfillmentState> Fulfillments);

    private sealed record FulfillmentState(FulfillmentKind Kind, FulfillmentStatus Status);
}
