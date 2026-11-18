using Gatehouse.Agent.Execution;
using Gatehouse.Agent.Storage;
using Gatehouse.Connector.Contracts.Protocol;
using Microsoft.Extensions.Logging;

namespace Gatehouse.Agent.Transport;

public sealed class PollLoop(
    GatewayClient client,
    CommandExecutor executor,
    PendingAckStore pending,
    TimeProvider time,
    ILogger<PollLoop> logger,
    int waitSeconds = 25)
{
    /// <summary>Partitions executed at the same time in one poll cycle.</summary>
    public const int MaxParallelPartitions = 8;

    private static readonly TimeSpan UnauthorizedRecheck = TimeSpan.FromMinutes(5);

    public Backoff Backoff { get; } = new();

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var outcome = await RunOnceAsync(ct);
            TimeSpan? delay = outcome switch
            {
                PollCycleOutcome.Unauthorized => UnauthorizedRecheck,
                PollCycleOutcome.Backoff => Backoff.Next(LastRetryAfter),
                _ => null,
            };
            if (delay is { } d)
            {
                try
                {
                    await Task.Delay(d, time, ct);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    internal TimeSpan? LastRetryAfter { get; private set; }

    public async Task<PollCycleOutcome> RunOnceAsync(CancellationToken ct)
    {
        try
        {
            LastRetryAfter = null;
            await FlushAsync(ct);
            var response = await client.PollAsync(new PollRequest(50, waitSeconds), ct);
            Backoff.Reset();
            // Order matters only within a partition (connector-protocol.md §3): partitions run in parallel, so a lockdown's
            // per-door commands (partition door:{id}) no longer wait for each other on a slow PACS (lab finding #4).
            var partitions = response.Commands.GroupBy(c => c.PartitionKey, StringComparer.Ordinal).ToList();
            await Parallel.ForEachAsync(
                partitions,
                new ParallelOptions { MaxDegreeOfParallelism = MaxParallelPartitions, CancellationToken = ct },
                async (partition, token) =>
                {
                    foreach (var command in partition)
                    {
                        using var span = AgentTelemetry.StartExecute(command);
                        var ack = await executor.ExecuteAsync(command, token);
                        span?.SetTag("gatehouse.ack_status", ack.Status.ToString());
                        await pending.AddAsync(ack, time.GetUtcNow(), token);
                    }
                });

            await FlushAsync(ct);
            return PollCycleOutcome.Continue;
        }
        catch (AgentUnauthorizedException ex)
        {
            logger.LogError("Gateway rejected the agent (HTTP {Status}); polling paused, rechecking in 5 minutes", ex.StatusCode);
            return PollCycleOutcome.Unauthorized;
        }
        catch (GatewayTransientException ex)
        {
            LastRetryAfter = ex.RetryAfter;
            logger.LogWarning("Gateway call failed ({Reason}); backing off", ex.Message);
            return PollCycleOutcome.Backoff;
        }
    }

    private async Task FlushAsync(CancellationToken ct)
    {
        var acks = await pending.ListAsync(100, ct);
        if (acks.Count == 0)
        {
            return;
        }

        var response = await client.AckAsync(new AckRequest(acks), ct);
        foreach (var result in response.Results)
        {
            if (!result.Accepted)
            {
                logger.LogWarning("Ack for {CommandId} rejected with {Code}; dropping it", result.CommandId, result.Code);
            }

            await pending.RemoveAsync(result.CommandId, ct);
        }
    }
}
