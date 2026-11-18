using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Adapters;
using Gatehouse.Connector.Contracts.Events;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Transport;

public sealed class HeartbeatLoop(
    GatewayClient client,
    IPacsAdapter adapter,
    PendingAckStore pending,
    PendingEventStore events,
    AgentOptions options,
    TimeProvider time,
    ILogger<HeartbeatLoop> logger,
    ScheduledRevocationStore? schedules = null)
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    public Backoff Backoff { get; } = new();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var delay = Interval;
            try
            {
                await SendOnceAsync(ct);
                Backoff.Reset();
            }
            catch (AgentUnauthorizedException ex)
            {
                logger.LogError("Heartbeat rejected (HTTP {Status}); rechecking in 5 minutes", ex.StatusCode);
                delay = TimeSpan.FromMinutes(5);
            }
            catch (GatewayTransientException ex)
            {
                logger.LogWarning("Heartbeat failed ({Reason})", ex.Message);
                delay = Backoff.Next(ex.RetryAfter);
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

    public async Task SendOnceAsync(CancellationToken ct)
    {
        var capabilities = adapter.Capabilities;
        var depth = await pending.CountAsync(ct) + await events.CountAsync(ct);
        var refused = schedules is null ? null : await schedules.RefusedAsync(ct);
        await client.HeartbeatAsync(
            new Heartbeat(options.AgentVersion, new AdapterInfo(
                capabilities.Vendor, capabilities.AdapterVersion), capabilities, depth, true,
                time.GetUtcNow(), refused),
            ct);
    }
}
