using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Adapters;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Transport;

/// <summary>
/// Streams adapter events into <see cref="PendingEventStore"/>. The adapter advances its cursor only after an item
/// is stored (the store happens before the enumerator resumes), so a crash re-delivers rather than loses events.
/// </summary>
public sealed class EventPumpLoop(
    IPacsAdapter adapter,
    PendingEventStore store,
    Action onStored,
    TimeProvider time,
    ILogger<EventPumpLoop> logger)
{
    private static readonly TimeSpan RestartDelay = TimeSpan.FromSeconds(1);

    public Backoff Backoff { get; } = new();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = RestartDelay;
            try
            {
                await PumpOnceAsync(ct);
                Backoff.Reset();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                // host boundary: an adapter failure must not stop the agent
                logger.LogWarning("Event stream failed ({Reason}); restarting", ex.Message);
                delay = Backoff.Next();
            }

            try
            {
                await Task.Delay(delay, time, ct);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <summary>Consumes the adapter stream until it ends; returns how many events were stored.</summary>
    public async Task<int> PumpOnceAsync(CancellationToken ct)
    {
        var count = 0;
        await foreach (var ev in adapter.StreamEventsAsync(ct))
        {
            await store.AddAsync(ev, ct);
            count++;
            onStored();
        }

        return count;
    }
}
