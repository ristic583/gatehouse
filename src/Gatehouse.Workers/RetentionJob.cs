using Gatehouse.Application.People.Retention;
using Gatehouse.Application.Tenancy;

namespace Gatehouse.Workers;

/// <summary>Runs GDPR retention (<see cref="RetentionRunner"/>) for every active tenant 5 minutes after start, then daily.</summary>
public sealed partial class RetentionJob(
    IServiceScopeFactory rootScopes,
    ITenantScopeFactory tenantScopes,
    ITenantLoop tenantLoop,
    TimeProvider time,
    ILogger<RetentionJob> logger) : BackgroundService
{
    private static readonly TimeSpan FirstRunDelay = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Interval = TimeSpan.FromDays(1);

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

        using var timer = new PeriodicTimer(Interval, time);
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
                var result = await scope.ServiceProvider.GetRequiredService<RetentionRunner>().RunTenantAsync(time.GetUtcNow(), token);
                LogRun(tenantId.Value, result.PhotosDeleted, result.PeopleAnonymized);
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
        Message = "Retention run for {Tenant}: {PhotosDeleted} photos deleted, {PeopleAnonymized} people anonymized")]
    private partial void LogRun(string tenant, int photosDeleted, int peopleAnonymized);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention run failed for {Tenant}")]
    private partial void LogTenantFailed(Exception ex, string tenant);

    [LoggerMessage(Level = LogLevel.Error, Message = "Retention job failed")]
    private partial void LogFailed(Exception ex);
}
