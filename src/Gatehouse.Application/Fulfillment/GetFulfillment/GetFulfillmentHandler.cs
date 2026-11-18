using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.GetFulfillment;

public sealed class GetFulfillmentHandler(ITenantDb db, ICurrentUser me, FulfillmentDetailLoader loader)
{
    public async Task<Result<FulfillmentDetailDto>> HandleAsync(GetFulfillmentQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var id = new FulfillmentId(query.FulfillmentId);
        var fulfillment = await db.Fulfillments.AsNoTracking()
            .HandledBy(me, db.Entitlements, db.Areas, db.Floors)
            .SingleOrDefaultAsync(f => f.Id == id, ct);
        if (fulfillment is null)
        {
            return Error.NotFound("fulfillment.not_found", "Fulfillment not found.");
        }

        return await loader.LoadAsync(fulfillment, ct);
    }
}
