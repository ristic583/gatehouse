using Gatehouse.Application.Access.Dtos;
using Gatehouse.Application.Authorization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Identity;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.Approvals;

/// <summary>Steps waiting for my decision, oldest request first. Never contains my own requests.</summary>
public sealed class GetApprovalInboxHandler(ITenantDb db, ICurrentUser currentUser, AccessRequestDtoBuilder dtos, TimeProvider time)
{
    public async Task<Result<IReadOnlyList<ApprovalInboxItemDto>>> HandleAsync(GetApprovalInboxQuery query, CancellationToken ct)
    {
        var me = await currentUser.RequirePersonIdAsync(ct);
        var now = time.GetUtcNow();
        var entitlements = await db.Entitlements.AsNoTracking()
            .AwaitingDecisionBy(me, db.Areas)
            .Where(e => e.EffectiveEnd > now) // ended before the lifecycle run expired it: it can no longer be approved (M11-17)
            .OrderBy(e => e.CreatedAt)
            .ToListAsync(ct);
        var areaIds = entitlements.Select(e => e.AreaId).Distinct().ToList();
        var levels = await db.Areas.AsNoTracking()
            .Where(a => areaIds.Contains(a.Id))
            .Join(db.SecurityLevels, a => a.SecurityLevelId, l => l.Id, (a, l) => new { a.Id, l.Name })
            .ToDictionaryAsync(x => x.Id, x => x.Name, ct);
        var requests = await dtos.BuildAsync(entitlements, ct);

        IReadOnlyList<ApprovalInboxItemDto> items = entitlements.Zip(requests, (e, r) =>
        {
            var step = e.Steps.Single(s => s.Status == ApprovalStepStatus.Pending);
            return new ApprovalInboxItemDto(
                r.EntitlementId,
                step.Id.Value,
                step.Order,
                step.ApproverKind.ToString(),
                r.Subject,
                r.RequestedBy,
                r.Area,
                levels[e.AreaId],
                r.RequestedStart,
                r.RequestedEnd,
                r.EffectiveEnd,
                r.CappedToEligibility,
                r.Justification,
                r.CreatedAt);
        }).ToList();
        return Result.Success(items);
    }
}
