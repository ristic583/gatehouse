using System.Globalization;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Access;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.People;
using Gatehouse.Domain.Platform;
using Gatehouse.Infrastructure.Persistence.Catalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.Platform;

/// <summary>
/// Writes <c>tenant_statistics</c> (counts only, ADR-009 §2) for every active tenant via the scope factory (tenancy rule 3),
/// and marks the first admin activated once their person is linked to an identity.
/// </summary>
public sealed partial class TenantStatisticsCollector(
    IServiceScopeFactory scopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    ILogger<TenantStatisticsCollector> logger)
{
    public const string OnboardingCompletedKey = "onboarding.completed_at";

    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantSummary> tenants;
        await using (var scope = scopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct);
        }

        var done = 0;
        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            try
            {
                await CollectAsync(tenantId, token);
                Interlocked.Increment(ref done);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
#pragma warning disable CA1031 // host boundary: one failing tenant must not stop the others
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogTenantFailed(ex, tenantId.Value);
            }
        }, ct);

        return done;
    }

    public async Task CollectAsync(TenantId tenantId, CancellationToken ct)
    {
        await using var catalogScope = scopes.CreateAsyncScope();
        var catalog = catalogScope.ServiceProvider.GetRequiredService<CatalogDbContext>();
        var provisioning = await catalog.TenantProvisionings.SingleOrDefaultAsync(p => p.TenantId == tenantId, ct);
        var now = time.GetUtcNow();

        var stats = new TenantStatistics { TenantId = tenantId, ComputedAt = now };
        bool adminLinked;
        await using (var scope = await tenantScopes.CreateScopeAsync(tenantId, ct))
        {
            var db = scope.ServiceProvider.GetRequiredService<ITenantDb>();
            stats.People = await db.Persons.CountAsync(p => p.AnonymizedAt == null, ct);
            stats.Employees = await db.Persons.CountAsync(p => p.AnonymizedAt == null && p.Type == PersonType.Employee, ct);
            stats.TenantAdmins = await db.RoleAssignments.Where(r => r.Role == AssignedRole.TenantAdmin)
                .Select(r => r.PersonId)
                .Distinct()
                .CountAsync(ct);
            stats.LinkedPeople = await db.Persons.CountAsync(p => p.IdentitySubject != null, ct);
            stats.Sites = await db.Sites.CountAsync(ct);
            stats.Floors = await db.Floors.CountAsync(ct);
            stats.Areas = await db.Areas.CountAsync(ct);
            stats.Doors = await db.Doors.CountAsync(ct);
            stats.ActiveEntitlements = await db.Entitlements.CountAsync(e => e.Status == EntitlementStatus.Approved, ct);
            stats.PendingRequests = await db.Entitlements.CountAsync(e => e.Status == EntitlementStatus.PendingApproval, ct);
            stats.OpenAlarms = await db.Alarms.CountAsync(a => a.AcknowledgedAt == null, ct);
            var cutoff = now.AddHours(-1);
            stats.UnconfirmedFulfillments = await db.Fulfillments.CountAsync(
                f => (f.Status == FulfillmentStatus.Desired || f.Status == FulfillmentStatus.Pending) && f.CreatedAt < cutoff, ct);

            var instances = await db.PacsInstances.AsNoTracking().ToListAsync(ct);
            stats.PacsInstances = instances.Count;
            var connectorMode = instances.Where(i => i.FulfillmentMode == FulfillmentMode.Connector).ToList();
            stats.ConnectorsOnline = connectorMode.Count(i => i.IsOnline(now));
            stats.ConnectorsNeverSeen = connectorMode.Count(i => i.LastHeartbeatAt is null);
            stats.ConnectorsOffline = connectorMode.Count - stats.ConnectorsOnline - stats.ConnectorsNeverSeen;

            var completed = await db.TenantSettings.AsNoTracking()
                .Where(s => s.Key == OnboardingCompletedKey)
                .Select(s => s.Value)
                .SingleOrDefaultAsync(ct);
            stats.OnboardingCompletedAt = DateTimeOffset.TryParse(
                completed, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                ? at
                : null;

            var adminEmail = provisioning?.AdminEmail;
            adminLinked = adminEmail is not null && await db.Persons.AnyAsync(p => p.Email == adminEmail && p.IdentitySubject != null, ct);
        }

        var existing = await catalog.TenantStatistics.SingleOrDefaultAsync(s => s.TenantId == tenantId, ct);
        if (existing is null)
        {
            catalog.TenantStatistics.Add(stats);
        }
        else
        {
            catalog.Entry(existing).CurrentValues.SetValues(stats);
        }

        if (adminLinked && provisioning is { AdminActivatedAt: null })
        {
            provisioning.MarkAdminActivated(now);
        }

        await catalog.SaveChangesAsync(ct);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Statistics collection failed for {TenantId}")]
    private partial void LogTenantFailed(Exception ex, string tenantId);
}
