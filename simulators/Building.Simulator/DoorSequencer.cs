using System.Collections.Concurrent;

namespace Building.Simulator;

/// <summary>Plays timed door event sequences in the background. All delays are armed up-front, relative to the start.</summary>
public sealed class DoorSequencer(DoorControllerRouter router, TimeProvider timeProvider, ILogger<DoorSequencer> logger)
{
    private readonly ConcurrentDictionary<Guid, Task> _running = new();

    public void Play(string doorRef, params (TimeSpan At, string Type)[] steps)
    {
        ArgumentNullException.ThrowIfNull(steps);
        var id = Guid.NewGuid();
        var timers = steps.Select(s => (s.Type, Due: s.At <= TimeSpan.Zero ? Task.CompletedTask : Task.Delay(s.At, timeProvider))).ToList();
        _running[id] = RunAsync(id, doorRef, timers);
    }

    /// <summary>Completes when every sequence started so far has finished.</summary>
    public Task WhenIdleAsync() => Task.WhenAll(_running.Values);

    private async Task RunAsync(Guid id, string doorRef, List<(string Type, Task Due)> timers)
    {
        try
        {
            foreach (var (type, due) in timers)
            {
                await due;
                var controller = await router.ForAsync(doorRef, CancellationToken.None);
                await controller.DoorEventAsync(doorRef, type, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Door sequence on {DoorRef} failed", doorRef);
        }
        finally
        {
            _running.TryRemove(id, out _);
        }
    }
}
