using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Fulfillment.Dtos;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Fulfillment.ListFulfillments;

public sealed class ListFulfillmentsHandler(ITenantDb db, ICurrentUser me, FulfillmentDtoBuilder builder)
{
    public async Task<Result<IReadOnlyList<FulfillmentQueueItemDto>>> HandleAsync(ListFulfillmentsQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var scoped = db.Fulfillments.AsNoTracking().HandledBy(me, db.Entitlements, db.Areas, db.Floors);
        List<Domain.Fulfillment.Fulfillment> rows;
        switch (query.View.ToLowerInvariant())
        {
            case "open":
                rows = await scoped
                    .Where(f => f.Status == FulfillmentStatus.Desired || f.Status == FulfillmentStatus.Pending
                    || f.Status == FulfillmentStatus.Failed
                        || f.Status == FulfillmentStatus.Drift)
                    .OrderBy(f => f.Priority == CommandPriority.High ? 0 : 1)
                    .ThenBy(f => f.CreatedAt)
                    .ToListAsync(ct);
                break;
            case "done":
                rows = await scoped
                    .Where(f => f.Status == FulfillmentStatus.Confirmed || f.Status == FulfillmentStatus.Superseded)
                    .OrderByDescending(f => f.UpdatedAt)
                    .Take(50)
                    .ToListAsync(ct);
                break;
            default:
                return Error.Validation("validation.failed", "View must be 'open' or 'done'.");
        }

        IReadOnlyList<FulfillmentQueueItemDto> items = (await builder.BuildAsync(rows, ct)).Select(x => x.Item).ToList();
        return Result.Success(items);
    }
}
