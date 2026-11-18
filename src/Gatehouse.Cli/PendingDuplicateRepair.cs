using Gatehouse.Application.Audit;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gatehouse.Cli;

/// <summary>
/// <c>repair-pending-duplicates</c>: finds (person, area) pairs with more than one <c>PendingApproval</c> entitlement, which block
/// the unique index <see cref="UniqueIndex"/> (migration <c>PendingRequestUnique</c>). With apply, every pair keeps its oldest
/// request; the others are cancelled with the system actor and audited <c>entitlement.cancelled</c> (<c>reason: duplicate_pending</c>).
/// </summary>
public sealed class PendingDuplicateRepair(IServiceProvider services, TextWriter output)
{
    public const string UniqueIndex = "ux_entitlements_pending_person_area";
    public const string AuditComponent = "cli.repair-pending-duplicates";
    public const string Reason = "duplicate_pending";

    public static bool IsUniqueIndexViolation(string? migrationError) =>
        migrationError is not null && migrationError.Contains(UniqueIndex, StringComparison.Ordinal);

    public async Task<int> RunAsync(TenantId? tenantId, bool apply, CancellationToken ct)
    {
        IReadOnlyList<TenantId> tenants;
        if (tenantId is { } single)
        {
            tenants = [single];
        }
        else
        {
            await using var catalogScope = services.CreateAsyncScope();
            tenants = [
                .. (await catalogScope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct)).Select(t => t.Id)];
        }

        foreach (var tenant in tenants)
        {
            await RepairTenantAsync(tenant, apply, ct);
        }

        return CliRunner.Success;
    }

    private async Task RepairTenantAsync(TenantId tenantId, bool apply, CancellationToken ct)
    {
        await using var scope = await services.GetRequiredService<ITenantScopeFactory>().CreateScopeAsync(tenantId, ct);
        var db = scope.ServiceProvider.GetRequiredService<ITenantDb>();
        var pending = await db.Entitlements.AsNoTracking()
            .Where(e => e.Status == EntitlementStatus.PendingApproval)
            .Select(e => new { e.Id, e.PersonId, e.AreaId, e.CreatedAt })
            .ToListAsync(ct);
        var duplicates = pending
            .GroupBy(e => (e.PersonId, e.AreaId))
            .Where(g => g.Count() > 1)
            .Select(g => g.OrderBy(e => e.CreatedAt).ThenBy(e => e.Id.Value).ToList())
            .ToList();
        if (duplicates.Count == 0)
        {
            await output.WriteLineAsync($"{tenantId}: no duplicate pending requests");
            return;
        }

        foreach (var group in duplicates)
        {
            await output.WriteLineAsync(
                $"{tenantId}: person {group[0].PersonId.Value} area {group[0].AreaId.Value}: keep {group[0].Id.Value}, "
                + $"{(apply ? "cancel" : "would cancel")} {string.Join(", ", group.Skip(1).Select(e => e.Id.Value))}");
        }

        if (!apply)
        {
            await output.WriteLineAsync($"{tenantId}: {duplicates.Count} duplicate pair(s); nothing changed (add --apply to cancel)");
            return;
        }

        var cancelIds = duplicates.SelectMany(g => g.Skip(1).Select(e => e.Id)).ToList();
        var audit = scope.ServiceProvider.GetRequiredService<IAuditLog>();
        var now = scope.ServiceProvider.GetRequiredService<TimeProvider>().GetUtcNow();
        foreach (var entitlement in await db.Entitlements.Where(e => cancelIds.Contains(e.Id)).ToListAsync(ct))
        {
            // Cancelled on the requester's behalf (the domain allows only the requester); the audit names the system actor.
            if (entitlement.Cancel(entitlement.RequestedById, now).IsSuccess)
            {
                audit.Record(AuditActor.ForSystem(AuditComponent), "entitlement.cancelled", AuditSubjects.Entitlement,
                    entitlement.Id.Value.ToString(), new { reason = Reason });
            }
        }

        await db.SaveChangesAsync(ct);
        await output.WriteLineAsync($"{tenantId}: cancelled {cancelIds.Count} duplicate pending request(s)");
    }
}
