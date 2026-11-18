using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Execution;

/// <summary>
/// Revokes access at <c>ValidTo</c> for adapters without native validity (adapter-interface.md §7).
/// Applied → done; retryable failure or exception → retried next tick; a non-retryable failure is never abandoned (GH-CONN-03): it is
/// logged as an error, reported in every heartbeat until it succeeds (staff task <c>ScheduledRemovalRefused</c>) and retried after
/// <see cref="PermanentFailureDelay"/>.
/// </summary>
public sealed class ScheduledRevocationLoop(
    IPacsAdapter adapter,
    ScheduledRevocationStore store,
    TimeProvider time,
    ILogger<ScheduledRevocationLoop> logger,
    TimeSpan? interval = null)
{
    /// <summary>How long a removal the PACS refused permanently waits before the next attempt.</summary>
    public static readonly TimeSpan PermanentFailureDelay = TimeSpan.FromMinutes(15);

    private const int BatchSize = 100;
    private readonly TimeSpan _interval = interval ?? TimeSpan.FromSeconds(30);

    public async Task RunAsync(CancellationToken ct)
    {
        if (adapter.Capabilities.NativeValidity)
        {
            return;
        }

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunOnceAsync(ct);
                await Task.Delay(_interval, time, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    /// <summary>Processes due rows once; returns how many were marked done.</summary>
    public async Task<int> RunOnceAsync(CancellationToken ct)
    {
        if (adapter.Capabilities.NativeValidity)
        {
            return 0;
        }

        var done = 0;
        foreach (var due in await store.DueAsync(time.GetUtcNow(), BatchSize, ct))
        {
            AdapterResult result;
            try
            {
                result = await adapter.RevokeAccessAsync(new AccessRevoke(due.PersonRef, due.CardNumber, due.VendorAccessLevelRef), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // host boundary: an adapter bug must not stop the loop; retried next tick
                logger.LogWarning(
                    "Scheduled revocation of {Level} for card {Card} threw ({Reason}); retrying", due.VendorAccessLevelRef,
                    CardNumberMask.Mask(due.CardNumber), ex.GetType().Name);
                continue;
            }

            if (result.Status == AdapterStatus.Applied)
            {
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation(
                        "Scheduled revocation of {Level} for card {Card} applied", due.VendorAccessLevelRef,
                        CardNumberMask.Mask(due.CardNumber));
                }
            }
            else if (result.Error is { Retryable: false } error)
            {
                logger.LogError(
                    "Scheduled revocation of {Level} for card {Card} was refused: {Code}; access may remain, retrying in {Delay}",
                    due.VendorAccessLevelRef, CardNumberMask.Mask(due.CardNumber), error.Code, PermanentFailureDelay);
                await store.RecordRefusalAsync(due.PersonRef, due.VendorAccessLevelRef, error.Code, time.GetUtcNow(), ct);
                await store.PostponeAsync(due.Id, time.GetUtcNow() + PermanentFailureDelay, ct);
                continue;
            }
            else
            {
                logger.LogWarning(
                    "Scheduled revocation of {Level} for card {Card} failed: {Code}; retrying", due.VendorAccessLevelRef,
                    CardNumberMask.Mask(due.CardNumber), result.Error?.Code);
                continue;
            }

            await store.MarkDoneAsync(due.Id, ct);
            done++;
        }

        return done;
    }
}
