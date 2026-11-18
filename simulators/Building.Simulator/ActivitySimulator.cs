namespace Building.Simulator;

/// <summary>Background foot traffic: every <c>Simulator:ActivityIntervalSeconds</c> (default 20, 0 = off) swipes a random active assignment's door; 1 in 5 uses an unknown card.</summary>
public sealed class ActivitySimulator(IServiceScopeFactory scopes, IConfiguration configuration, TimeProvider timeProvider, ILogger<ActivitySimulator> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var seconds = configuration.GetValue("Simulator:ActivityIntervalSeconds", 20);
        if (seconds <= 0)
        {
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(seconds), timeProvider);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var controller = scope.ServiceProvider.GetRequiredService<LegacyPacsControllerClient>();
                var simulator = scope.ServiceProvider.GetRequiredService<BuildingSimulator>();
                var active = (await controller.ListAssignmentsAsync(stoppingToken)).Where(a => a.IsActive).ToList();
                if (active.Count == 0)
                {
                    continue;
                }

#pragma warning disable CA5394 // Simulated foot traffic, not security-relevant randomness.
                var pick = active[Random.Shared.Next(active.Count)];
                var unknownCard = Random.Shared.Next(5) == 0;
#pragma warning restore CA5394
                var door = "D-" + (pick.AccessLevelRef.StartsWith("AL-", StringComparison.Ordinal) ? pick.AccessLevelRef[3..] : pick.AccessLevelRef);
                var command = unknownCard
                    ? new SwipeCommand(door, CardNumber: "UNKNOWN-" + Guid.NewGuid().ToString("N")[..8])
                    : new SwipeCommand(door, CardNumber: pick.CardNumber, PersonRef: pick.PersonRef, Direction: NextDirection(door, pick.PersonRef));
                await simulator.SwipeAsync(command, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Simulated swipe failed");
            }
        }
    }

    /// <summary>People inside per door: a person who went in walks out on their next swipe at that door (occupancy demo).</summary>
    private readonly HashSet<string> _inside = new(StringComparer.Ordinal);

    private string NextDirection(string door, string? personRef)
    {
        var key = door + "|" + personRef;
        return _inside.Remove(key) ? "Out" : _inside.Add(key) ? "In" : "In";
    }
}
