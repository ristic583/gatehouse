using System.Collections.Concurrent;
using System.Globalization;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.People;
using Gatehouse.Infrastructure.Persistence.Tenant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Infrastructure.LiveBuilding;

/// <summary>Keeps today + 7 days of <c>door_events</c> partitions per tenant and drops days past retention (ADR-006).</summary>
public sealed partial class DoorEventPartitionMaintenance(
    ICatalogReader catalog,
    ITenantScopeFactory scopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    IConfiguration configuration,
    ILogger<DoorEventPartitionMaintenance> logger)
{
    public const int DaysAhead = 8;
    public const int DefaultRetentionDays = 90;

    public int RetentionDays => configuration.GetValue("LiveBuilding:DoorEventRetentionDays", DefaultRetentionDays);

    /// <summary>The tenant's <c>retention.door_event_days</c> (M9) when set and in range, else <see cref="RetentionDays"/>.</summary>
    private async Task<int> TenantRetentionDaysAsync(TenantDbContext db, CancellationToken ct)
    {
        var stored = await db.TenantSettings.AsNoTracking()
            .Where(s => s.Key == RetentionSettings.DoorEventDaysKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        var (min, max) = RetentionSettings.Ranges[RetentionSettings.DoorEventDaysKey];
        return int.TryParse(stored, NumberStyles.Integer, CultureInfo.InvariantCulture, out var days) && days >= min && days <= max
            ? days
            : RetentionDays;
    }

    /// <summary>Returns the number of dropped partitions per tenant.</summary>
    public async Task<IReadOnlyDictionary<string, int>> RunOnceAsync(CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var result = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
        var tenants = await catalog.GetActiveTenantsAsync(ct);
        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            await using var scope = await scopes.CreateScopeAsync(tenantId, token);
            var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
            await DoorEventPartitions.EnsureAsync(db, today, DaysAhead, token);
            var cutoff = today.AddDays(-await TenantRetentionDaysAsync(db, token));
            var dropped = await DoorEventPartitions.DropOlderThanAsync(db, cutoff, token);
            result[tenantId.Value] = dropped;
            if (dropped > 0)
            {
                LogDropped(tenantId.Value, dropped, cutoff);
            }

            var stray = await DoorEventPartitions.CountDefaultRowsAsync(db, token);
            if (stray > 0)
            {
                LogDefaultRows(tenantId.Value, stray);
            }
        }, ct);

        return result;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Dropped {Count} door event partitions before {Cutoff} for tenant {Tenant}")]
    private partial void LogDropped(string tenant, int count, DateOnly cutoff);

    [LoggerMessage(
        Level = LogLevel.Warning, Message = "door_events_default holds {Rows} rows for tenant {Tenant}; daily partitions are missing")]
    private partial void LogDefaultRows(string tenant, long rows);
}
