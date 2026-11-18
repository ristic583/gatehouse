namespace Building.Simulator;

/// <summary>Door physics and demo scenarios on top of the PACS controllers (LegacyPACS, CloudPACS via <see cref="DoorControllerRouter"/>). <c>fire-alarm</c> and <c>pacs-slow</c> are LegacyPACS-only.</summary>
public sealed class BuildingSimulator(DoorControllerRouter router, DoorSequencer sequencer)
{
    public const string DefaultDoorRef = "D-CPH-LOBBY";
    public const int SlowDelayMs = 8000;

    public static readonly string[] Scenarios = ["forced-door", "held-open", "fire-alarm", "pacs-slow"];

    /// <summary>Asks the PACS for a decision; on Granted the door opens, closes and relocks. Null when the door is unknown.</summary>
    public async Task<ControllerDecision?> SwipeAsync(SwipeCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        var controller = await router.ForAsync(command.DoorRef, ct);
        var decision = await controller.SwipeAsync(command, ct);
        if (decision?.Decision == "Granted")
        {
            sequencer.Play(command.DoorRef, (TimeSpan.FromSeconds(1), "DoorOpened"), (TimeSpan.FromSeconds(4), "DoorClosed"), (TimeSpan.FromSeconds(5), "DoorRelocked"));
        }

        return decision;
    }

    /// <summary>Starts a scenario; false when the name is unknown.</summary>
    public async Task<bool> RunScenarioAsync(string name, string? doorRef, CancellationToken ct)
    {
        var door = string.IsNullOrWhiteSpace(doorRef) ? DefaultDoorRef : doorRef;
        switch (name)
        {
            case "forced-door":
                sequencer.Play(door, (TimeSpan.Zero, "DoorForced"), (TimeSpan.FromSeconds(10), "DoorClosed"));
                return true;
            case "held-open":
                sequencer.Play(door, (TimeSpan.Zero, "DoorUnlocked"), (TimeSpan.Zero, "DoorOpened"), (TimeSpan.FromSeconds(2), "DoorHeldOpen"), (TimeSpan.FromSeconds(20), "DoorClosed"));
                return true;
            case "fire-alarm":
                foreach (var d in await router.Legacy.ListDoorsAsync(ct))
                {
                    await router.Legacy.DoorEventAsync(d.DoorRef, "DoorUnlocked", ct);
                }

                return true;
            case "pacs-slow":
                await router.Legacy.SetSlowAsync(SlowDelayMs, ct);
                return true;
            default:
                return false;
        }
    }

    /// <summary>Ends a scenario that has a clear step; false when it has none.</summary>
    public async Task<bool> ClearScenarioAsync(string name, CancellationToken ct)
    {
        switch (name)
        {
            case "fire-alarm":
                foreach (var d in await router.Legacy.ListDoorsAsync(ct))
                {
                    await router.Legacy.DoorEventAsync(d.DoorRef, "DoorRelocked", ct);
                }

                return true;
            case "pacs-slow":
                await router.Legacy.ClearChaosAsync(ct);
                return true;
            default:
                return false;
        }
    }
}
