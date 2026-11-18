using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Credentials.CardCommands;

/// <summary>
/// The PACS instances that hold a person as cardholder: <c>Connector</c>-mode instances where the person has a <c>Grant</c>
/// fulfillment in <c>Pending</c> or <c>Confirmed</c>.
/// </summary>
public sealed class CardCommandTargets(ITenantDb db)
{
    public async Task<IReadOnlyList<CardCommandTarget>> ForPersonAsync(PersonId personId, CancellationToken ct)
    {
        var instanceIds = db.Fulfillments
            .Where(f => f.Kind == FulfillmentKind.Grant
                && (f.Status == FulfillmentStatus.Pending || f.Status == FulfillmentStatus.Confirmed)
                && db.Entitlements.Any(e => e.Id == f.EntitlementId && e.PersonId == personId))
            .Select(f => f.PacsInstanceId);
        return await db.PacsInstances.AsNoTracking()
            .Where(p => p.FulfillmentMode == FulfillmentMode.Connector && instanceIds.Contains(p.Id))
            .OrderBy(p => p.Name)
            .Select(p => new CardCommandTarget(p.Id, p.CapabilitiesJson))
            .ToListAsync(ct);
    }
}
