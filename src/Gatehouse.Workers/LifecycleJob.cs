using Gatehouse.Application.People.Lifecycle;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.Workers;

/// <summary>Runs <see cref="LifecycleRunner"/> for every active tenant 2 minutes after start, then every <c>Lifecycle:IntervalMinutes</c>
/// (15).</summary>
public sealed partial class LifecycleJob(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<LifecycleJob> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(2);

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

        var minutes = configuration.GetValue("Lifecycle:IntervalMinutes", 15);
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
                var result = await scope.ServiceProvider.GetRequiredService<LifecycleRunner>().RunTenantAsync(time.GetUtcNow(), token);
                LogRun(tenantId.Value, result.Terminated, result.Expired, result.RenewalsRequested);
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

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Lifecycle run for {Tenant}: {Terminated} terminated, {Expired} expired, {RenewalsRequested} renewals requested")]
    private partial void LogRun(string tenant, int terminated, int expired, int renewalsRequested);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lifecycle run failed for {Tenant}")]
    private partial void LogTenantFailed(Exception ex, string tenant);

    [LoggerMessage(Level = LogLevel.Error, Message = "Lifecycle job failed")]
    private partial void LogFailed(Exception ex);
}
