using System.Threading.Channels;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Transport;

/// <summary>Uploads pending door events in batches; events are removed only after the gateway accepted them (202).</summary>
public sealed class EventSendLoop(
    GatewayClient client,
    PendingEventStore store,
    TimeProvider time,
    ILogger<EventSendLoop> logger)
{
    public const int BatchSize = 500;

    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan UnauthorizedRecheck = TimeSpan.FromMinutes(5);

    private readonly Channel<bool> _signal = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public Backoff Backoff { get; } = new();

    internal int LastSent { get; private set; }

    internal TimeSpan? LastRetryAfter { get; private set; }

    /// <summary>Wakes the loop early (new events were stored).</summary>
    public void Notify() => _signal.Writer.TryWrite(true);

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var outcome = await RunOnceAsync(ct);
            try
            {
                switch (outcome)
                {
                    case PollCycleOutcome.Unauthorized:
                        await Task.Delay(UnauthorizedRecheck, time, ct);
                        break;
                    case PollCycleOutcome.Backoff:
                        await Task.Delay(Backoff.Next(LastRetryAfter), time, ct);
                        break;
                    case PollCycleOutcome.Continue when LastSent < BatchSize:
                        await WaitForSignalAsync(ct);
                        break;
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
        }
    }

    public async Task<PollCycleOutcome> RunOnceAsync(CancellationToken ct)
    {
        LastSent = 0;
        LastRetryAfter = null;
        var batch = await store.TakeAsync(BatchSize, ct);
        if (batch.Count == 0)
        {
            return PollCycleOutcome.Continue;
        }

        try
        {
            await client.SendEventsAsync(new DoorEventsRequest(batch), ct);
            await store.RemoveAsync(batch.Select(e => e.EventId), ct);
            Backoff.Reset();
            LastSent = batch.Count;
            return PollCycleOutcome.Continue;
        }
        catch (AgentUnauthorizedException ex)
        {
            logger.LogError("Gateway rejected the agent (HTTP {Status}); event upload paused, rechecking in 5 minutes", ex.StatusCode);
            return PollCycleOutcome.Unauthorized;
        }
        catch (GatewayTransientException ex)
        {
            LastRetryAfter = ex.RetryAfter;
            logger.LogWarning("Event upload failed ({Reason}); backing off", ex.Message);
            return PollCycleOutcome.Backoff;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError("Event upload rejected ({Reason}); keeping events and backing off", ex.Message);
            return PollCycleOutcome.Backoff;
        }
    }

    private async Task WaitForSignalAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(Idle, time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await _signal.Reader.ReadAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // idle tick
        }
    }
}
