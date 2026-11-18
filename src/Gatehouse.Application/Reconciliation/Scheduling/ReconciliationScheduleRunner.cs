using System.Collections.Concurrent;
using Gatehouse.Application.Common.Persistence;
using Gatehouse.Application.Connectors;
using Gatehouse.Application.Reconciliation.FailReconciliation;
using Gatehouse.Application.Reconciliation.RequestReconciliation;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;
using Gatehouse.Domain.Common;
using Gatehouse.Domain.Fulfillment;
using Gatehouse.Domain.Reconciliation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gatehouse.Application.Reconciliation.Scheduling;

/// <summary>
/// Per active tenant: fails reports running longer than <see cref="ReconciliationScheduleOptions.TimeoutHours"/>
/// (<c>reconciliation.timed_out</c>), then requests a report for every connector instance with no report requested within
/// <see cref="ReconciliationScheduleOptions.IntervalDays"/> (instances that cannot export or already run are skipped).
/// </summary>
public sealed partial class ReconciliationScheduleRunner(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    IOptions<ReconciliationScheduleOptions> options,
    ILogger<ReconciliationScheduleRunner> logger)
{
    public const string TimedOutCode = "reconciliation.timed_out";

    private static readonly AuditActor Actor = AuditActor.ForSystem("reconciliation-scheduler");

    public async Task<IReadOnlyDictionary<TenantId, ReconciliationScheduleResult>> RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantSummary> tenants;
        await using (var scope = rootScopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct);
        }

        var now = time.GetUtcNow();
        var result = new ConcurrentDictionary<TenantId, ReconciliationScheduleResult>();
        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            try
            {
                result[tenantId] = await RunTenantAsync(tenantId, now, token);
            }
            catch (OperationCanceledException)
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

        return result;
    }

    private async Task<ReconciliationScheduleResult> RunTenantAsync(TenantId tenantId, DateTimeOffset now, CancellationToken ct)
    {
        await using var scope = await tenantScopes.CreateScopeAsync(tenantId, ct);
        var services = scope.ServiceProvider;
        var db = services.GetRequiredService<ITenantDb>();
        var settings = options.Value;

        var stuckBefore = now.AddHours(-settings.TimeoutHours);
        var stuck = await db.ReconciliationReports.AsNoTracking()
            .Where(r => (
                r.Status == ReconciliationStatus.Requested || r.Status == ReconciliationStatus.Exporting
                    || r.Status == ReconciliationStatus.Comparing)
                && r.RequestedAt < stuckBefore)
            .Select(r => r.Id).ToListAsync(ct);
        var fail = services.GetRequiredService<FailReconciliationHandler>();
        foreach (var reportId in stuck)
        {
            await fail.HandleAsync(reportId, TimedOutCode, $"The reconciliation did not finish within {settings.TimeoutHours} hours.", ct);
        }

        if (stuck.Count > 0)
        {
            await db.SaveChangesAsync(ct);
        }

        var dueBefore = now.AddDays(-settings.IntervalDays);
        var due = await db.PacsInstances
            .Where(p => p.FulfillmentMode == FulfillmentMode.Connector
                && !db.ReconciliationReports.Any(r => r.PacsInstanceId == p.Id && r.RequestedAt > dueBefore))
            .ToListAsync(ct);
        var starter = services.GetRequiredService<ReconciliationStarter>();
        var started = new List<PacsInstanceId>();
        foreach (var instance in due)
        {
            if ((await starter.StartAsync(instance, null, Actor, ct)).IsSuccess)
            {
                started.Add(instance.Id);
            }
        }

        if (started.Count > 0)
        {
            await db.SaveChangesAsync(ct);
            var signal = services.GetRequiredService<IConnectorCommandSignal>();
            foreach (var instanceId in started)
            {
                await signal.NotifyAsync(tenantId, instanceId, ct);
            }
        }

        if (stuck.Count > 0 || started.Count > 0)
        {
            LogRun(tenantId.Value, stuck.Count, started.Count);
        }

        return new ReconciliationScheduleResult(stuck.Count, started.Count);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Reconciliation scheduler for tenant {TenantKey}: {TimedOut} timed out, {Requested} requested")]
    private partial void LogRun(string tenantKey, int timedOut, int requested);

    [LoggerMessage(Level = LogLevel.Error, Message = "Reconciliation scheduler failed for tenant {TenantKey}")]
    private partial void LogTenantFailed(Exception ex, string tenantKey);
}
