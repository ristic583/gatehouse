using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Microsoft.EntityFrameworkCore;

namespace Gatehouse.Application.Access.Expiry;

/// <summary>
/// Lifecycle step (GH-ACCESS-02): a request still pending when its window has ended can no longer be approved; it becomes
/// <c>Expired</c> (system actor <c>expiry</c>, audit <c>request.expired_unapproved</c>) and leaves the approval inboxes. Saves once.
/// </summary>
public sealed class UnapprovedRequestExpiry(ITenantDb db, IAuditLog audit)
{
    private static readonly AuditActor Actor = AuditActor.ForSystem("expiry");

    public async Task<int> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var ended = await db.Entitlements.Where(e => e.Status == EntitlementStatus.PendingApproval && e.EffectiveEnd <= now)
            .ToListAsync(ct);
        var expired = 0;
        foreach (var entitlement in ended.Where(e => e.ExpireUnapproved(now).IsSuccess))
        {
            audit.Record(Actor, "request.expired_unapproved", AuditSubjects.Entitlement, entitlement.Id.Value.ToString(),
                new { effectiveEnd = entitlement.EffectiveEnd.ToString("O", System.Globalization.CultureInfo.InvariantCulture) });
            expired++;
        }

        if (expired > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        return expired;
    }
}
