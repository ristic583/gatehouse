using Gatehouse.Application.People.HrSync;
using Gatehouse.Application.Tenancy;
using Gatehouse.Domain.Audit;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="HrSyncRunner"/> for every active tenant 1 minute after start, then every <c>HrSync:IntervalMinutes</c>
/// (60).</summary>
public sealed partial class HrSyncJob(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<HrSyncJob> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(1);
    private static readonly AuditActor Actor = AuditActor.ForSystem("hr-sync");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(FirstRunDelay, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }

        var minutes = configuration.GetValue("HrSync:IntervalMinutes", 60);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, minutes)), time);
        do
        {
            try
            {
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // host boundary (worker loop): the failed run is logged and retried on the next tick
            catch (Exception ex)
#pragma warning restore CA1031
            {
                LogFailed(ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        IReadOnlyList<TenantSummary> tenants;
        await using (var scope = rootScopes.CreateAsyncScope())
        {
            tenants = await scope.ServiceProvider.GetRequiredService<ICatalogReader>().GetActiveTenantsAsync(ct);
        }

        await tenantLoop.ForEachAsync(tenants.Select(t => t.Id), async (tenantId, token) =>
        {
            try
            {
                await using var scope = await tenantScopes.CreateScopeAsync(tenantId, token);
                var result = await scope.ServiceProvider.GetRequiredService<HrSyncRunner>().RunAsync(Actor, time.GetUtcNow(), token);
                if (result.IsFailure)
                {
                    LogNoFeed(tenantId.Value);
                    return;
                }

                LogRun(tenantId.Value, result.Value.Created, result.Value.Updated, result.Value.Unchanged, result.Value.Skipped);
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
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "No HR feed for {Tenant}")]
    private partial void LogNoFeed(string tenant);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "HR sync for {Tenant}: {Created} created, {Updated} updated, {Unchanged} unchanged, {Skipped} skipped")]
    private partial void LogRun(string tenant, int created, int updated, int unchanged, int skipped);

    [LoggerMessage(Level = LogLevel.Error, Message = "HR sync failed for {Tenant}")]
    private partial void LogTenantFailed(Exception ex, string tenant);

    [LoggerMessage(Level = LogLevel.Error, Message = "HR sync job failed")]
    private partial void LogFailed(Exception ex);
}
